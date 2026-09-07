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

/**
 * 結構不變式：同一人同一天最多一格。回他當天已在的另一區 areaId，沒有就 null。
 * 這不是約束（不在約束設定裡、不能停用），跟「同一格兩個人」同一層次，
 * 所以在寫入時直接拒絕，而不是產生違規。
 */
function otherAreaOnDate(
  schedule: ReturnType<typeof ensureSchedule>,
  staffId: string,
  date: string,
  exceptAreaId: string,
): string | null {
  for (const area of store.areas) {
    if (area.id === exceptAreaId) continue
    if (schedule.duties.get(dutyKey(area.id, date)) === staffId) return area.id
  }
  return null
}

function staffAlreadyOnDuty(staffId: string, date: string, areaId: string) {
  const name = store.staff.find((s) => s.id === staffId)?.name ?? staffId
  return errorResponse(409, 'STAFF_ALREADY_ON_DUTY', `${name} 在 ${date} 已排在另一區`, { staffId, date, areaId })
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
    if (body.staffId) {
      const other = otherAreaOnDate(schedule, body.staffId, body.date, body.areaId)
      if (other) return staffAlreadyOnDuty(body.staffId, body.date, other)
    }

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

    // 對調後 A 的人落到 b 格、B 的人落到 a 格；日期不同時可能撞到同人同日另一區。
    // 兩格互為對方的來源，檢查時把對方那格排除。
    if (staffA && body.a.date !== body.b.date) {
      const other = otherAreaOnDate(schedule, staffA, body.b.date, body.b.areaId)
      if (other && other !== body.a.areaId) return staffAlreadyOnDuty(staffA, body.b.date, other)
    }
    if (staffB && body.a.date !== body.b.date) {
      const other = otherAreaOnDate(schedule, staffB, body.a.date, body.a.areaId)
      if (other && other !== body.b.areaId) return staffAlreadyOnDuty(staffB, body.a.date, other)
    }

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
    const hasHardViolations = violations.some((v) => v.severity === 'hard')
    if (hasHardViolations && !body?.acknowledgeViolations) {
      return errorResponse(409, 'HARD_VIOLATIONS_PRESENT', '仍有硬約束違規，需明確確認才能發布')
    }

    schedule.status = 'published'
    schedule.revision++
    schedule.publishedAt = new Date().toISOString()
    const carryOver = computeCarryOverEntries(store, ym)
    store.carryOver.set(nextYearMonth(ym), carryOver)

    return HttpResponse.json({
      status: schedule.status,
      publishedAt: schedule.publishedAt,
      revision: schedule.revision,
      carryOver,
    })
  }),

  http.get('/api/schedules/:ym/export', ({ params, request }) => {
    const ym = params.ym as string
    const layout = new URL(request.url).searchParams.get('layout') ?? 'area-by-day'
    const schedule = store.schedules.get(ym)
    const duties = schedule ? scheduleToDuties(schedule) : []

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
        bestObjective: 100 - i * 5,
        bestBound: 95 - i * 5,
        gap: i === variantCount ? 0 : Number((5 / i).toFixed(2)),
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
        bestObjective: 100,
        bestBound: 100,
        gap: 0,
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
    const busy = [...store.solverJobs.values()].some((j) => j.status === 'queued' || j.status === 'running')
    if (busy) return errorResponse(409, 'SOLVER_BUSY', '已有求解工作在執行中')

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
    const variants: Variant[] = store.variants.get(params.jobId as string) ?? []
    return HttpResponse.json({ variants })
  }),

  http.post('/api/schedules/:ym/apply-variant', async ({ params, request }) => {
    const ym = params.ym as string
    const body = (await request.json()) as { jobId: string; variantId: string }

    const existing = store.schedules.get(ym)
    if (existing?.status === 'published') {
      return errorResponse(409, 'SCHEDULE_ALREADY_PUBLISHED', '該月值班表已發布，不可整份套用變體')
    }

    const variant = (store.variants.get(body.jobId) ?? []).find((v) => v.id === body.variantId)
    if (!variant) return errorResponse(404, 'NOT_FOUND', '找不到指定的變體')

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
    if (store.staff.some((s) => s.employeeNo === body.employeeNo)) {
      return errorResponse(409, 'EMPLOYEE_NO_TAKEN', `員編 ${body.employeeNo} 已被使用`)
    }
    const seq = store.nextStaffSeq++
    const staff: Staff = {
      id: `staff-${String(seq).padStart(3, '0')}`,
      employeeNo: body.employeeNo,
      name: body.name,
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
    const staff = store.staff.find((s) => s.id === id)
    if (!staff) return errorResponse(404, 'NOT_FOUND', '找不到人員')

    staff.employeeNo = body.employeeNo
    staff.name = body.name
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
