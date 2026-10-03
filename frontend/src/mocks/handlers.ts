import { http, HttpResponse } from 'msw'
import type {
  AreaSettings,
  Calendar,
  CalendarDayOverride,
  ConstraintSettings,
  CreateSolverJobRequest,
  EligibilityMatrix,
  ErrorCode,
  MonthlyOverride,
  MutationResult,
  PointRules,
  RankSettings,
  Schedule,
  ScheduleSummary,
  SetDutyRequest,
  SolverJob,
  Staff,
  StaffCounts,
  StaffStatus,
  StaffWrite,
  SwapDutiesRequest,
  Variant,
} from '@/api/types'
import { calendarYearFacts } from './fixtures/calendar'
import { variantLabels, variantWeightProfiles } from './fixtures/constraints'
import { eligibleAreaTypesOf } from './fixtures/ranks'
import {
  computeCandidates,
  computeCarryOverEntries,
  computeDayDetail,
  computeFeasibility,
  computePointBoard,
  computeValidationResult,
  computeVacancies,
  computeViolations,
  datesOfYearMonth,
  dutyKey,
  ensureSchedule,
  generateVariants,
  getCalendarDay,
  nextYearMonth,
  parseDutyKey,
  previousMonthWarnings,
  scheduleToDuties,
} from './domain'
import { store, type SolverJobState } from './store'

/**
 * Mock 端點，與 api-contract.yaml 的 42 個操作一一對應，依契約的 tag 分段。
 *
 * **SSE 備註**：`/solver-jobs/{jobId}/stream` 用 `ReadableStream` 模擬
 * `text/event-stream`，本機瀏覽器測試沒問題；但 MSW 對 `EventSource` 攔截的
 * 支援仍有限。若實際串接 `frontend/src/realtime.ts` 時發現收不到事件，
 * 退而求其次的作法是改用輪詢 `GET /solver-jobs/{jobId}`（進度快照的形狀相同）。
 */

// ---------------------------------------------------------------------------
// 共用小工具
// ---------------------------------------------------------------------------

function errorResponse(status: number, code: ErrorCode, message: string, details?: Record<string, unknown>) {
  return HttpResponse.json({ error: { code, message, details } }, { status })
}

function toScheduleResponse(schedule: ReturnType<typeof ensureSchedule>): Schedule {
  return {
    yearMonth: schedule.yearMonth,
    status: schedule.status,
    revision: schedule.revision,
    publishedVersion: schedule.publishedVersion,
    publishedAt: schedule.publishedAt,
    dayCount: datesOfYearMonth(schedule.yearMonth).length,
    staffCount: store.staff.filter((s) => s.status === 'active').length,
    areas: store.areas,
    duties: scheduleToDuties(schedule),
  }
}

/**
 * 本次改動的格子。清空的格子也要帶回（staffId 為 null），前端才知道是哪一格被清了。
 * changed 以 [areaId, date] 指定，不從 dutyKey 反解。
 */
function toMutationResult(
  schedule: ReturnType<typeof ensureSchedule>,
  changed: Array<{ areaId: string; date: string }>,
): MutationResult {
  return {
    revision: schedule.revision,
    duties: changed.map(({ areaId, date }) => ({
      areaId,
      date,
      staffId: schedule.duties.get(dutyKey(areaId, date)) ?? null,
      cellKey: `area:${areaId}:${date}`,
    })),
    violations: computeViolations(store, schedule.yearMonth),
  }
}

// ---------------------------------------------------------------------------
// health
// ---------------------------------------------------------------------------

const healthHandlers = [http.get('/api/health', () => HttpResponse.json({ status: 'ok (mock)' }))]

// ---------------------------------------------------------------------------
// 值班表
// ---------------------------------------------------------------------------

const scheduleHandlers = [
  http.get('/api/schedules', () => {
    const months: ScheduleSummary[] = [...store.schedules.values()]
      .sort((a, b) => a.yearMonth.localeCompare(b.yearMonth))
      .map((schedule) => ({
        yearMonth: schedule.yearMonth,
        status: schedule.status,
        revision: schedule.revision,
        publishedVersion: schedule.publishedVersion,
        publishedAt: schedule.publishedAt,
        hardViolationCount: computeViolations(store, schedule.yearMonth).filter((v) => v.severity === 'hard').length,
      }))
    return HttpResponse.json({ months })
  }),

  http.get('/api/schedules/:ym', ({ params }) => {
    const ym = params.ym as string
    const schedule = store.schedules.get(ym)
    if (!schedule) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)
    return HttpResponse.json(toScheduleResponse(schedule))
  }),

  http.patch('/api/schedules/:ym/duties', async ({ params, request }) => {
    const ym = params.ym as string
    const body = (await request.json()) as SetDutyRequest

    const area = store.areas.find((a) => a.id === body.areaId)
    if (!area) return errorResponse(422, 'INVALID_REQUEST', '區域不存在')
    if (!body.date.startsWith(ym)) return errorResponse(422, 'INVALID_REQUEST', '日期不在本月')
    if (body.staffId) {
      const staff = store.staff.find((s) => s.id === body.staffId)
      if (!staff) return errorResponse(422, 'INVALID_REQUEST', '人員不存在')
    }

    // 該月尚無值班表時自動建立一份空草稿——這是「從空白手排」的入口。
    // 已發布的值班表也可以改，revision 照常遞增。
    const schedule = ensureSchedule(store, ym)
    // 同人同日兩區照常寫入，由違規清單的 X1 回報（#68）；把關在發布。
    const key = dutyKey(body.areaId, body.date)
    if (body.staffId) schedule.duties.set(key, body.staffId)
    else schedule.duties.delete(key)
    schedule.revision++

    return HttpResponse.json(toMutationResult(schedule, [{ areaId: body.areaId, date: body.date }]))
  }),

  http.post('/api/schedules/:ym/duties/swap', async ({ params, request }) => {
    const ym = params.ym as string
    const body = (await request.json()) as SwapDutiesRequest
    const schedule = store.schedules.get(ym)
    if (!schedule) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)

    const keyA = dutyKey(body.a.areaId, body.a.date)
    const keyB = dutyKey(body.b.areaId, body.b.date)
    const staffA = schedule.duties.get(keyA)
    const staffB = schedule.duties.get(keyB)

    if (body.a.areaId === body.b.areaId && body.a.date === body.b.date) {
      return errorResponse(422, 'INVALID_REQUEST', '對調的兩格是同一格')
    }

    // 對調後同人同日兩區也照常寫入，由違規清單的 X1 回報（#68）。
    if (staffB) schedule.duties.set(keyA, staffB)
    else schedule.duties.delete(keyA)
    if (staffA) schedule.duties.set(keyB, staffA)
    else schedule.duties.delete(keyB)
    schedule.revision++

    return HttpResponse.json(toMutationResult(schedule, [body.a, body.b]))
  }),

  http.post('/api/schedules/:ym/validate', ({ params }) => {
    const ym = params.ym as string
    if (!store.schedules.has(ym)) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)
    return HttpResponse.json(computeValidationResult(store, ym))
  }),

  http.post('/api/schedules/:ym/publish', async ({ params, request }) => {
    const ym = params.ym as string
    const body = (await request.json().catch(() => null)) as { acknowledgeViolations?: boolean } | null
    // 不會憑空建一份空表發布
    const schedule = store.schedules.get(ym)
    if (!schedule) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)

    const violations = computeViolations(store, ym)
    // X1 同人同日兩區：不論 acknowledgeViolations 為何都擋，先於 HARD_VIOLATIONS_PRESENT（#68）
    const doubleBooked = violations.filter((v) => v.code === 'X1_STAFF_DOUBLE_BOOKED').length
    if (doubleBooked > 0) {
      return errorResponse(409, 'DOUBLE_BOOKING_PRESENT', `仍有 ${doubleBooked} 項同一人同一天排在兩區，排除後才能發布`, {
        doubleBookingCount: doubleBooked,
        hardViolationCount: violations.filter((v) => v.severity === 'hard').length,
      })
    }
    const hasHardViolations = violations.some((v) => v.severity === 'hard')
    if (hasHardViolations && !body?.acknowledgeViolations) {
      return errorResponse(409, 'HARD_VIOLATIONS_PRESENT', '仍有硬約束違規，需明確確認才能發布')
    }

    schedule.status = 'published'
    schedule.revision++
    schedule.publishedVersion++
    schedule.publishedAt = new Date().toISOString()
    const carryOver = computeCarryOverEntries(store, ym)
    store.carryOver.set(nextYearMonth(ym), carryOver)

    return HttpResponse.json({
      status: schedule.status,
      publishedAt: schedule.publishedAt,
      revision: schedule.revision,
      publishedVersion: schedule.publishedVersion,
      carryOver,
    })
  }),

  http.get('/api/schedules/:ym/export', ({ params, request }) => {
    const ym = params.ym as string
    const layout = new URL(request.url).searchParams.get('layout') ?? 'area-by-day'
    // 與後端一致：layout 不認得是 422、該月尚無值班表是 404
    if (layout !== 'area-by-day' && layout !== 'day-by-staff') {
      return errorResponse(422, 'INVALID_REQUEST', `layout 只能是 area-by-day 或 day-by-staff：${layout}`)
    }
    const schedule = store.schedules.get(ym)
    if (!schedule) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)
    // 同人同日兩區（X1）不論草稿或已發布都不匯出，與後端 DoubleBookingGuard 一致（#68）
    const doubleBooked = computeViolations(store, ym).filter((v) => v.code === 'X1_STAFF_DOUBLE_BOOKED').length
    if (doubleBooked > 0) {
      return errorResponse(409, 'DOUBLE_BOOKING_PRESENT', `仍有 ${doubleBooked} 項同一人同一天排在兩區，排除後才能匯出`, {
        doubleBookingCount: doubleBooked,
      })
    }
    const duties = scheduleToDuties(schedule)

    // 簡化：不是真正的 xlsx，回一份最小的 CSV 位元組，content-type 與檔名照契約。
    // layout 目前不影響輸出內容——mock 端不實作版面差異。
    void layout
    const header = 'areaId,date,staffId\n'
    const rows = duties.map((d) => `${d.areaId},${d.date},${d.staffId}`).join('\n')
    const csv = header + rows + '\n'

    return new HttpResponse(csv, {
      status: 200,
      headers: {
        'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        'Content-Disposition': `attachment; filename="duty-${ym}.xlsx"`,
      },
    })
  }),

  http.get('/api/schedules/:ym/violations', ({ params, request }) => {
    const ym = params.ym as string
    const url = new URL(request.url)
    const severity = url.searchParams.get('severity')
    const date = url.searchParams.get('date')
    if (!store.schedules.has(ym)) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)

    let violations = computeViolations(store, ym)
    if (severity) violations = violations.filter((v) => v.severity === severity)
    if (date) violations = violations.filter((v) => v.cellKeys.some((k) => k.endsWith(`:${date}`)))

    return HttpResponse.json({ violations })
  }),
]

// ---------------------------------------------------------------------------
// 由值班表推導的檢視
// ---------------------------------------------------------------------------

const viewHandlers = [
  http.get('/api/schedules/:ym/point-board', ({ params }) => {
    const ym = params.ym as string
    if (!store.schedules.has(ym)) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)
    return HttpResponse.json({ groups: computePointBoard(store, ym) })
  }),

  http.get('/api/schedules/:ym/days/:date', ({ params }) => {
    const ym = params.ym as string
    const date = params.date as string
    // 與後端一致：該月尚無值班表也是 404，不憑空當成空白表
    if (!store.schedules.has(ym)) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)
    if (!date.startsWith(ym)) return errorResponse(404, 'NOT_FOUND', `${date} 不屬於 ${ym}`)
    return HttpResponse.json(computeDayDetail(store, ym, date))
  }),

  http.get('/api/schedules/:ym/vacancies', ({ params }) => {
    const ym = params.ym as string
    if (!store.schedules.has(ym)) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)
    return HttpResponse.json(computeVacancies(store, ym))
  }),

  http.get('/api/schedules/:ym/candidates', ({ params, request }) => {
    const ym = params.ym as string
    const url = new URL(request.url)
    const areaId = url.searchParams.get('areaId') ?? ''
    const date = url.searchParams.get('date') ?? ''
    if (!store.schedules.has(ym)) return errorResponse(404, 'NOT_FOUND', `找不到 ${ym} 的值班表`)
    return HttpResponse.json({ candidates: computeCandidates(store, ym, areaId, date) })
  }),
]

// ---------------------------------------------------------------------------
// 不可排班日
// ---------------------------------------------------------------------------

const blockedDayHandlers = [
  http.get('/api/blocked-days/:ym', ({ params }) => {
    const ym = params.ym as string
    const monthlyCap = 16
    const entries = store.blockedDays.get(ym) ?? []

    const byStaff = store.staff
      .filter((s) => s.status === 'active')
      .map((s) => {
        const count = entries.filter((e) => e.staffId === s.id).length
        return { staffId: s.id, count, remaining: monthlyCap - count }
      })
    // byStaff 是表格（每位在職人員一列，0 也列）；byDate 是清單（只列有登記的日期）

    const dateCounts = new Map<string, number>()
    for (const e of entries) dateCounts.set(e.date, (dateCounts.get(e.date) ?? 0) + 1)
    const byDate = [...dateCounts.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([date, count]) => ({ date, count }))

    return HttpResponse.json({ yearMonth: ym, monthlyCap, entries, byStaff, byDate })
  }),

  http.put('/api/blocked-days/:ym/:staffId/:date', ({ params }) => {
    const ym = params.ym as string
    const staffId = params.staffId as string
    const date = params.date as string
    const monthlyCap = 16

    const entries = store.blockedDays.get(ym) ?? []
    const already = entries.some((e) => e.staffId === staffId && e.date === date)
    if (!already) {
      const currentCount = entries.filter((e) => e.staffId === staffId).length
      if (currentCount >= monthlyCap) {
        return errorResponse(409, 'BLOCKED_DAY_CAP_EXCEEDED', `${staffId} 本月不可排班日已達上限 ${monthlyCap} 天`)
      }
      entries.push({ staffId, date })
      store.blockedDays.set(ym, entries)
    }

    const count = entries.filter((e) => e.staffId === staffId).length
    const dateCount = entries.filter((e) => e.date === date).length
    return HttpResponse.json({
      staffTotals: { count, remaining: monthlyCap - count },
      dateTotals: { count: dateCount },
    })
  }),

  http.delete('/api/blocked-days/:ym/:staffId/:date', ({ params }) => {
    const ym = params.ym as string
    const staffId = params.staffId as string
    const date = params.date as string
    const monthlyCap = 16

    const entries = (store.blockedDays.get(ym) ?? []).filter((e) => !(e.staffId === staffId && e.date === date))
    store.blockedDays.set(ym, entries)

    const count = entries.filter((e) => e.staffId === staffId).length
    const dateCount = entries.filter((e) => e.date === date).length
    return HttpResponse.json({
      staffTotals: { count, remaining: monthlyCap - count },
      dateTotals: { count: dateCount },
    })
  }),

  http.get('/api/blocked-days/:ym/feasibility', ({ params }) => {
    const ym = params.ym as string
    return HttpResponse.json(computeFeasibility(store, ym))
  }),
]

// ---------------------------------------------------------------------------
// 求解
// ---------------------------------------------------------------------------

/**
 * 收斂間隙 `|obj − bound| / |obj|`，四捨五入到小數 4 位——與真後端
 * `SolverJobService.GapOf` 同一份公式，回的是**比例（0–1），不是百分比**（issue #46）。
 */
function gapOf(objective: number | null, bound: number | null): number | null {
  if (objective === null || bound === null) return null
  if (objective === 0) return 0
  return Number((Math.abs(objective - bound) / Math.abs(objective)).toFixed(4))
}

const jobTimers = new Map<string, ReturnType<typeof setTimeout>[]>()

function clearJobTimers(jobId: string) {
  for (const t of jobTimers.get(jobId) ?? []) clearTimeout(t)
  jobTimers.delete(jobId)
}

function toSolverJobResponse(job: SolverJobState): SolverJob {
  return {
    jobId: job.jobId,
    yearMonth: job.yearMonth,
    status: job.status,
    variantCount: job.variantCount,
    elapsedSec: job.elapsedSec,
    scale: job.scale,
    constraintCount: job.constraintCount,
    progress: job.progress,
    warnings: job.warnings,
    failureReason: job.failureReason,
  }
}

/** 序列模擬 3 份變體的求解：每份約 2 秒，總長約 6 秒（ADR-0003）。 */
function scheduleSolverJob(jobId: string, ym: string, variantCount: number, timeLimitSecPerVariant: number) {
  const startMs = Date.now()
  const timers: ReturnType<typeof setTimeout>[] = []
  // 終態快照沿用最後一份變體的 objective／bound，不是憑空的 100/100——
  // 跟 running 時的最後一筆數字接得起來，gap 也照同一份 gapOf 算。
  let lastObjective = 100
  let lastBound = 95

  const startRunning = setTimeout(() => {
    const job = store.solverJobs.get(jobId)
    if (!job || job.status === 'cancelled') return
    job.status = 'running'
    job.elapsedSec = (Date.now() - startMs) / 1000
    job.progress = {
      jobId,
      status: 'running',
      variantIndex: 1,
      variantCount,
      elapsedSec: job.elapsedSec,
      timeLimitSec: timeLimitSecPerVariant,
      solutionCount: 1,
      bestObjective: null,
      bestBound: null,
      gap: null,
    }
  }, 300)
  timers.push(startRunning)

  for (let i = 1; i <= variantCount; i++) {
    const t = setTimeout(() => {
      const job = store.solverJobs.get(jobId)
      if (!job || job.status === 'cancelled') return
      const bestObjective = 100 - i * 5
      const bestBound = 95 - i * 5
      lastObjective = bestObjective
      lastBound = bestBound
      job.status = 'running'
      job.elapsedSec = (Date.now() - startMs) / 1000
      job.progress = {
        jobId,
        status: 'running',
        variantIndex: i,
        variantCount,
        elapsedSec: job.elapsedSec,
        timeLimitSec: timeLimitSecPerVariant,
        solutionCount: i + 1,
        bestObjective,
        bestBound,
        gap: gapOf(bestObjective, bestBound),
      }
    }, i * 2000)
    timers.push(t)
  }

  const finish = setTimeout(
    () => {
      const job = store.solverJobs.get(jobId)
      if (!job || job.status === 'cancelled') return
      const variants = generateVariants(store, ym, variantWeightProfiles, variantLabels, variantCount)
      store.variants.set(jobId, variants)
      job.status = 'succeeded'
      job.elapsedSec = (Date.now() - startMs) / 1000
      job.progress = {
        jobId,
        status: 'succeeded',
        variantIndex: variantCount,
        variantCount,
        elapsedSec: job.elapsedSec,
        timeLimitSec: timeLimitSecPerVariant,
        solutionCount: variantCount,
        bestObjective: lastObjective,
        bestBound: lastBound,
        gap: gapOf(lastObjective, lastBound),
      }
      clearJobTimers(jobId)
    },
    variantCount * 2000 + 500,
  )
  timers.push(finish)

  jobTimers.set(jobId, timers)
}

const solverHandlers = [
  http.post('/api/solver-jobs', async ({ request }) => {
    const body = (await request.json()) as CreateSolverJobRequest
    const busyJob = [...store.solverJobs.values()].find((j) => j.status === 'queued' || j.status === 'running')
    if (busyJob) return errorResponse(409, 'SOLVER_BUSY', '已有求解工作在執行中', { jobId: busyJob.jobId })

    const ym = body.yearMonth
    const variantCount = Math.min(Math.max(body.variantCount ?? 3, 1), 3)
    const timeLimitSecPerVariant = body.timeLimitSecPerVariant ?? 15
    const jobId = `job-${store.nextJobSeq++}`

    const activeStaff = store.staff.filter((s) => s.status === 'active').length
    const days = datesOfYearMonth(ym).length
    const job: SolverJobState = {
      jobId,
      yearMonth: ym,
      status: 'queued',
      variantCount,
      elapsedSec: 0,
      scale: { staff: activeStaff, areas: store.areas.length, days, variables: activeStaff * store.areas.length * days },
      constraintCount: {
        hard: store.constraints.hard.filter((h) => h.enabled).length,
        soft: store.constraints.soft.filter((s) => s.weight > 0).length,
      },
      progress: {
        jobId,
        status: 'queued',
        variantIndex: 0,
        variantCount,
        elapsedSec: 0,
        timeLimitSec: timeLimitSecPerVariant,
        solutionCount: 0,
        bestObjective: null,
        bestBound: null,
        gap: null,
      },
      warnings: previousMonthWarnings(store, ym),
      failureReason: null,
      timeLimitSecPerVariant,
      startedAtMs: Date.now(),
    }
    store.solverJobs.set(jobId, job)
    scheduleSolverJob(jobId, ym, variantCount, timeLimitSecPerVariant)

    return HttpResponse.json(toSolverJobResponse(job), { status: 202 })
  }),

  http.get('/api/solver-jobs/:jobId', ({ params }) => {
    const job = store.solverJobs.get(params.jobId as string)
    if (!job) return errorResponse(404, 'NOT_FOUND', '找不到求解工作')
    return HttpResponse.json(toSolverJobResponse(job))
  }),

  http.delete('/api/solver-jobs/:jobId', ({ params }) => {
    const job = store.solverJobs.get(params.jobId as string)
    if (!job) return errorResponse(404, 'NOT_FOUND', '找不到求解工作')
    if (job.status === 'queued' || job.status === 'running') {
      clearJobTimers(job.jobId)
      job.status = 'cancelled'
      // job.progress 是巢狀物件，頂層 status 改了它也要跟著改，否則回應裡外層
      // status: 'cancelled' 但 progress.status 還留著 'running'，前端與 SSE 讀到的不一致。
      job.progress = { ...job.progress, status: 'cancelled' }
    }
    return HttpResponse.json(toSolverJobResponse(job))
  }),

  http.get('/api/solver-jobs/:jobId/stream', ({ params }) => {
    const jobId = params.jobId as string
    const encoder = new TextEncoder()
    let stopped = false

    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        const push = () => {
          if (stopped) return
          const job = store.solverJobs.get(jobId)
          if (!job) {
            controller.close()
            stopped = true
            return
          }
          controller.enqueue(encoder.encode(`data: ${JSON.stringify(job.progress)}\n\n`))
          if (job.status === 'succeeded' || job.status === 'failed' || job.status === 'cancelled') {
            controller.close()
            stopped = true
            return
          }
          setTimeout(push, 1000)
        }
        push()
      },
      cancel() {
        stopped = true
      },
    })

    return new HttpResponse(stream, {
      headers: { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache' },
    })
  }),

  http.get('/api/solver-jobs/:jobId/variants', ({ params }) => {
    const jobId = params.jobId as string
    if (!store.solverJobs.has(jobId)) return errorResponse(404, 'NOT_FOUND', '找不到求解工作')
    const variants: Variant[] = store.variants.get(jobId) ?? []
    return HttpResponse.json({ variants })
  }),

  http.post('/api/schedules/:ym/apply-variant', async ({ params, request }) => {
    const ym = params.ym as string
    const body = (await request.json().catch(() => null)) as { jobId?: string; variantId?: string } | null
    if (!body?.jobId || !body?.variantId) {
      return errorResponse(422, 'INVALID_REQUEST', '本體缺 jobId 或 variantId')
    }

    const job = store.solverJobs.get(body.jobId)
    if (!job) return errorResponse(404, 'NOT_FOUND', '找不到求解工作')

    const variant = (store.variants.get(body.jobId) ?? []).find((v) => v.id === body.variantId)
    if (!variant) return errorResponse(404, 'NOT_FOUND', '找不到指定的變體')

    // 變體屬於建立求解工作時的那個月，跟路徑上的 ym 不同就是誤套用
    if (job.yearMonth !== ym) {
      return errorResponse(422, 'INVALID_REQUEST', `變體 ${body.variantId} 是 ${job.yearMonth} 的，不能套用到 ${ym}`)
    }

    const existing = store.schedules.get(ym)
    if (existing?.status === 'published') {
      return errorResponse(409, 'SCHEDULE_ALREADY_PUBLISHED', '該月值班表已發布，不可整份套用變體')
    }

    const schedule = ensureSchedule(store, ym)
    schedule.duties = new Map(variant.duties.map((d) => [dutyKey(d.areaId, d.date), d.staffId]))
    schedule.status = 'draft'
    schedule.revision++

    return HttpResponse.json(toScheduleResponse(schedule))
  }),
]

// ---------------------------------------------------------------------------
// 設定
// ---------------------------------------------------------------------------

const settingsHandlers = [
  http.get('/api/settings/areas', () => HttpResponse.json({ areaTypes: store.areaTypes, areas: store.areas })),

  http.put('/api/settings/areas', async ({ request }) => {
    const body = (await request.json()) as AreaSettings
    const newAreaIds = new Set(body.areas.map((a) => a.id))
    const newAreaTypeCodes = new Set(body.areaTypes.map((t) => t.code))

    for (const area of store.areas) {
      if (newAreaIds.has(area.id)) continue
      const inUse = [...store.schedules.values()].some((s) =>
        [...s.duties.keys()].some((key) => parseDutyKey(key).areaId === area.id),
      )
      if (inUse) return errorResponse(409, 'AREA_IN_USE', `區域 ${area.name} 仍被值班表引用，不可刪除`)
    }
    for (const type of store.areaTypes) {
      if (newAreaTypeCodes.has(type.code)) continue
      const inUse = body.areas.some((a) => a.areaTypeCode === type.code)
      if (inUse) return errorResponse(409, 'AREA_TYPE_IN_USE', `區域類型 ${type.name} 仍被區域引用，不可刪除`)
    }

    store.areaTypes = body.areaTypes.map((t) => ({ ...t }))
    store.areas = body.areas.map((a) => ({ ...a }))
    return HttpResponse.json({ areaTypes: store.areaTypes, areas: store.areas })
  }),

  http.get('/api/settings/ranks', () => HttpResponse.json({ ranks: store.ranks, groups: store.rankGroups })),

  http.put('/api/settings/ranks', async ({ request }) => {
    const body = (await request.json()) as RankSettings
    const newRankCodes = new Set(body.ranks.map((r) => r.code))
    const newGroupCodes = new Set(body.groups.map((g) => g.code))

    for (const rank of store.ranks) {
      if (newRankCodes.has(rank.code)) continue
      const usedByStaff = store.staff.some((s) => s.rankCode === rank.code)
      const usedByMatrix = Object.hasOwn(store.eligibilityMatrix.matrix, rank.code)
      const usedByConstraints = [...store.constraints.hard, ...store.constraints.soft].some(
        (c) => c.scope?.rankCodes?.includes(rank.code) || c.scope?.exemptRankCodes?.includes(rank.code),
      )
      if (usedByStaff || usedByMatrix || usedByConstraints) {
        return errorResponse(409, 'RANK_IN_USE', `身分 ${rank.name} 仍被引用，不可刪除`)
      }
    }
    for (const group of store.rankGroups) {
      if (newGroupCodes.has(group.code)) continue
      const usedByRanks = body.ranks.some((r) => r.groupCode === group.code)
      if (usedByRanks) return errorResponse(409, 'RANK_IN_USE', `身分組 ${group.name} 仍被身分引用，不可刪除`)
    }

    store.ranks = body.ranks.map((r) => ({ ...r }))
    store.rankGroups = body.groups.map((g) => ({ ...g }))
    return HttpResponse.json({ ranks: store.ranks, groups: store.rankGroups })
  }),

  http.get('/api/settings/eligibility-matrix', () => HttpResponse.json(store.eligibilityMatrix)),

  http.put('/api/settings/eligibility-matrix', async ({ request }) => {
    const body = (await request.json()) as EligibilityMatrix
    store.eligibilityMatrix = { matrix: structuredClone(body.matrix) }
    // eligibleAreaTypes 是唯讀衍生欄位，矩陣一變就重算全體人員。
    store.staff = store.staff.map((s) => ({ ...s, eligibleAreaTypes: eligibleAreaTypesOf(store.eligibilityMatrix.matrix, s.rankCode) }))
    return HttpResponse.json(store.eligibilityMatrix)
  }),

  http.get('/api/settings/point-rules', () => HttpResponse.json(store.pointRules)),

  http.put('/api/settings/point-rules', async ({ request }) => {
    const body = (await request.json()) as PointRules
    store.pointRules = structuredClone(body)
    return HttpResponse.json(store.pointRules)
  }),

  http.get('/api/settings/constraints', () => HttpResponse.json(store.constraints)),

  http.put('/api/settings/constraints', async ({ request }) => {
    const body = (await request.json()) as ConstraintSettings
    store.constraints = structuredClone(body)
    return HttpResponse.json(store.constraints)
  }),

  http.get('/api/settings/monthly-overrides/:ym', ({ params }) => {
    const ym = params.ym as string
    return HttpResponse.json(store.monthlyOverrides.get(ym) ?? { yearMonth: ym })
  }),

  http.put('/api/settings/monthly-overrides/:ym', async ({ params, request }) => {
    const ym = params.ym as string
    const body = (await request.json()) as MonthlyOverride
    const value: MonthlyOverride = { ...body, yearMonth: ym }
    store.monthlyOverrides.set(ym, value)
    return HttpResponse.json(value)
  }),

  http.get('/api/calendars/:year', ({ params }) => {
    const year = Number(params.year)
    const days = calendarYearFacts(year).map((facts) => getCalendarDay(store, facts.date))
    const response: Calendar = { year, days }
    return HttpResponse.json(response)
  }),

  http.patch('/api/calendars/:year/:date', async ({ params, request }) => {
    const date = params.date as string
    const body = (await request.json()) as CalendarDayOverride
    const merged = { ...store.calendarOverrides.get(date), ...body }
    store.calendarOverrides.set(date, merged)
    return HttpResponse.json(getCalendarDay(store, date))
  }),
]

// ---------------------------------------------------------------------------
// 人員
// ---------------------------------------------------------------------------

/**
 * 員編、姓名不得空白。真後端在 `RequestMapper.ToCommand`（`Required(dto.EmployeeNo, ...)`）
 * 就丟這個 422，發生在 `StaffCommands.UpdateAsync` 的 `RequireAsync`（404）之前——
 * PATCH 要先做這個檢查，順序才對得上。
 */
function validateBlankFields(write: StaffWrite) {
  if (!write.employeeNo?.trim()) return errorResponse(422, 'INVALID_REQUEST', '員編不得空白')
  if (!write.name?.trim()) return errorResponse(422, 'INVALID_REQUEST', '姓名不得空白')
  return null
}

/** 身分必須存在於 `store.ranks`。真後端在 `EnsureWriteValidAsync`，PATCH 是 404 之後才跑。 */
function validateRankExists(write: StaffWrite) {
  if (!store.ranks.some((r) => r.code === write.rankCode)) {
    return errorResponse(422, 'INVALID_REQUEST', `找不到身分 ${write.rankCode}`)
  }
  return null
}

/** POST 沒有先查存在性這件事，`EnsureWriteValidAsync` 從頭到尾一次跑完，兩段檢查合在一起即可。 */
function validateStaffWrite(write: StaffWrite) {
  return validateBlankFields(write) ?? validateRankExists(write)
}

/** 員編是否已被別人使用；`excludeId` 排除自己，PATCH 改回原值不算重複。 */
function staffEmployeeNoTaken(employeeNo: string, excludeId: string | null) {
  const trimmed = employeeNo.trim()
  return store.staff.some((s) => s.employeeNo === trimmed && s.id !== excludeId)
}

const staffHandlers = [
  http.get('/api/staff', ({ request }) => {
    const status = new URL(request.url).searchParams.get('status') as StaffStatus | null
    const items = status ? store.staff.filter((s) => s.status === status) : store.staff
    const counts: StaffCounts = {
      active: store.staff.filter((s) => s.status === 'active').length,
      inactive: store.staff.filter((s) => s.status === 'inactive').length,
    }
    return HttpResponse.json({ items, counts })
  }),

  http.post('/api/staff', async ({ request }) => {
    const body = (await request.json()) as StaffWrite
    const invalid = validateStaffWrite(body)
    if (invalid) return invalid
    if (staffEmployeeNoTaken(body.employeeNo, null)) {
      return errorResponse(409, 'EMPLOYEE_NO_TAKEN', `員編 ${body.employeeNo.trim()} 已被使用`)
    }
    const seq = store.nextStaffSeq++
    const staff: Staff = {
      id: `staff-${String(seq).padStart(3, '0')}`,
      employeeNo: body.employeeNo.trim(),
      name: body.name.trim(),
      rankCode: body.rankCode,
      status: 'active',
      eligibleAreaTypes: eligibleAreaTypesOf(store.eligibilityMatrix.matrix, body.rankCode),
    }
    store.staff.push(staff)
    return HttpResponse.json(staff, { status: 201 })
  }),

  http.patch('/api/staff/:id', async ({ params, request }) => {
    const id = params.id as string
    const body = (await request.json()) as StaffWrite

    // 順序對齊真後端：空白員編／姓名在 RequestMapper 就丟 422，比 UpdateAsync 的
    // RequireAsync（404）早；身分不存在與員編重複在 EnsureWriteValidAsync，是 404 之後才跑。
    const blank = validateBlankFields(body)
    if (blank) return blank

    const staff = store.staff.find((s) => s.id === id)
    if (!staff) return errorResponse(404, 'NOT_FOUND', '找不到人員')

    const rankInvalid = validateRankExists(body)
    if (rankInvalid) return rankInvalid
    // 員編重複檢查排除自己——員編改回原值或維持不變都不算重複。
    if (staffEmployeeNoTaken(body.employeeNo, id)) {
      return errorResponse(409, 'EMPLOYEE_NO_TAKEN', `員編 ${body.employeeNo.trim()} 已被使用`)
    }

    staff.employeeNo = body.employeeNo.trim()
    staff.name = body.name.trim()
    staff.rankCode = body.rankCode
    staff.eligibleAreaTypes = eligibleAreaTypesOf(store.eligibilityMatrix.matrix, body.rankCode)
    return HttpResponse.json(staff)
  }),

  http.delete('/api/staff/:id', ({ params }) => {
    const id = params.id as string
    const staff = store.staff.find((s) => s.id === id)
    if (!staff) return errorResponse(404, 'NOT_FOUND', '找不到人員')

    const hasDuties = [...store.schedules.values()].some((s) => [...s.duties.values()].includes(id))
    if (hasDuties) return errorResponse(409, 'STAFF_HAS_DUTIES', '已有值班紀錄，不可刪除，請改為停用')

    store.staff = store.staff.filter((s) => s.id !== id)
    // 不可排班日登記是求解輸入，不是歷史事實，級聯刪除。
    for (const [ym, entries] of store.blockedDays) {
      store.blockedDays.set(ym, entries.filter((e) => e.staffId !== id))
    }
    return new HttpResponse(null, { status: 204 })
  }),

  http.patch('/api/staff/:id/status', async ({ params, request }) => {
    const id = params.id as string
    const body = (await request.json()) as { status: StaffStatus }
    const staff = store.staff.find((s) => s.id === id)
    if (!staff) return errorResponse(404, 'NOT_FOUND', '找不到人員')
    staff.status = body.status
    return HttpResponse.json(staff)
  }),
]

export const handlers = [
  ...healthHandlers,
  ...scheduleHandlers,
  ...viewHandlers,
  ...blockedDayHandlers,
  ...solverHandlers,
  ...settingsHandlers,
  ...staffHandlers,
]
