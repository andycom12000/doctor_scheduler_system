import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computeVariantMetrics, dutyKey } from './domain'
import { cancelSolverJob, scheduleSolverJob, streamPayload, toSolverJobResponse } from './handlers'
import { resetStore, store, type SolverJobState } from './store'

beforeEach(() => resetStore())

const YM = '2026-03'

function groupOf(code: string) {
  return store.ranks.find((r) => r.code === code)!.groupCode
}

function activeNonNp() {
  return store.staff.filter((s) => s.status === 'active' && s.rankCode !== 'NP')
}

/** 3 月的週一～週三（四週）× 全部區域：每格都是「平日接平日」，每筆值班得同一個點數。 */
function weekdayCellPool() {
  const dates = ['02', '03', '04', '09', '10', '11', '16', '17', '18', '23', '24', '25'].map((d) => `2026-03-${d}`)
  return store.areas.flatMap((area) => dates.map((date) => dutyKey(area.id, date)))
}

function weekdayPoints(rankCode: string) {
  const pointType = store.ranks.find((r) => r.code === rankCode)!.pointType!
  return store.pointRules.fairness.tables[pointType]!.find((r) => r.today === 'weekday' && r.tomorrow === 'weekday')!
    .points
}

/** 依序從格子池配班：`counts` 的每個人各得 n 筆。 */
function assign(counts: [string, number][]) {
  const pool = weekdayCellPool()
  const duty = new Map<string, string>()
  let next = 0
  for (const [staffId, n] of counts) {
    for (let i = 0; i < n; i++) duty.set(pool[next++]!, staffId)
  }
  return duty
}

function enableS7() {
  store.constraints.soft.find((c) => c.code === 'S7_FAIRNESS_POINT')!.weight = 30
}

function fairnessOf(duty: Map<string, string>) {
  return computeVariantMetrics(store, YM, duty).fairnessPoint
}

describe('computeVariantMetrics.fairnessPoint', () => {
  it('S7 未啟用（出廠 weight 0）時是 null', () => {
    const a = activeNonNp()[0]!
    expect(fairnessOf(assign([[a.id, 2]]))).toBeNull()
  })

  it('各身分組內 max − min 再加總，不是全體加總（對齊 Domain ScheduleScores.FairnessByGroup）', () => {
    enableS7()
    const a = activeNonNp()[0]!
    const b = activeNonNp().find((s) => s.id !== a.id && groupOf(s.rankCode) === groupOf(a.rankCode))!
    const c = activeNonNp().find((s) => groupOf(s.rankCode) !== groupOf(a.rankCode))!
    const p = weekdayPoints(a.rankCode)
    expect(p).toBeGreaterThan(0)

    // A 組內其他人為 0：max − min = 2p − 0（舊算法全體加總會得 3p）。
    const duty = assign([
      [a.id, 2],
      [b.id, 1],
    ])
    expect(fairnessOf(duty)).toBe(2 * p)

    // 另一組的 C 也值一天：該組其他人為 0，max − min = 他自己的點數，兩組加總。
    const withC = assign([
      [a.id, 2],
      [b.id, 1],
      [c.id, 1],
    ])
    expect(fairnessOf(withC)).toBe(2 * p + weekdayPoints(c.rankCode))
  })

  it('停用者不進比較：組內在職者點數都 > 0，停用者的 0 不會把 min 拉到 0', () => {
    enableS7()
    const members = activeNonNp()
    const gc = groupOf(members[0]!.rankCode)
    const groupMembers = members.filter((s) => groupOf(s.rankCode) === gc)
    expect(groupMembers.length).toBeGreaterThanOrEqual(3)
    const retired = groupMembers[0]!
    const stay = groupMembers.slice(1)
    retired.status = 'inactive'
    // 在職者 k 排 (k % 2) + 1 天：min = p、max = 2p，差距 p
    const duty = assign(stay.map((s, k): [string, number] => [s.id, (k % 2) + 1]))
    expect(fairnessOf(duty)).toBe(weekdayPoints(stay[0]!.rankCode))
  })

  it('NP 不進比較', () => {
    enableS7()
    const np = store.staff.find((s) => s.rankCode === 'NP' && s.status === 'active')!
    const a = activeNonNp()[0]!
    const b = activeNonNp().find((s) => s.id !== a.id && groupOf(s.rankCode) === groupOf(a.rankCode))!
    const base = fairnessOf(
      assign([
        [a.id, 2],
        [b.id, 1],
      ]),
    )
    const withNp = fairnessOf(
      assign([
        [a.id, 2],
        [b.id, 1],
        [np.id, 3],
      ]),
    )
    expect(withNp).toBe(base)
  })

  it('只有一人的組貢獻 0', () => {
    enableS7()
    const a = activeNonNp()[0]!
    for (const s of store.staff) {
      if (s.id !== a.id && groupOf(s.rankCode) === groupOf(a.rankCode)) s.status = 'inactive'
    }
    expect(fairnessOf(assign([[a.id, 3]]))).toBe(0)
  })
})

describe('scheduleSolverJob progress.elapsedSec', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    // 不留未觸發的 timer：先中止還活著的工作（清 jobTimers），再清掉假時鐘
    for (const job of store.solverJobs.values()) cancelSolverJob(job)
    vi.clearAllTimers()
    vi.useRealTimers()
  })

  function startJob(): SolverJobState {
    const job: SolverJobState = {
      jobId: 'job-t',
      yearMonth: YM,
      status: 'queued',
      variantCount: 3,
      elapsedSec: 0,
      scale: { staff: 1, areas: 1, days: 1, variables: 1 },
      constraintCount: { hard: 0, soft: 0 },
      progress: {
        jobId: 'job-t',
        status: 'queued',
        variantIndex: 0,
        variantCount: 3,
        elapsedSec: 0,
        timeLimitSec: 15,
        solutionCount: 0,
        bestObjective: null,
        bestBound: null,
        gap: null,
      },
      warnings: [],
      failureReason: null,
      timeLimitSecPerVariant: 15,
      startedAtMs: Date.now(),
    }
    store.solverJobs.set(job.jobId, job)
    scheduleSolverJob(job.jobId, job.yearMonth, 3, 15)
    return job
  }

  it('進行中：progress 是本份耗時、換份歸零；job.elapsedSec 從轉 running 起算的總耗時', () => {
    const job = startJob()

    vi.advanceTimersByTime(300) // 轉 running：總耗時從這刻起算，不含 queued 的 300ms
    expect(job.progress.variantIndex).toBe(1)
    expect(job.progress.elapsedSec).toBe(0)
    expect(job.elapsedSec).toBe(0)

    vi.advanceTimersByTime(1700) // t=2000，第 1 份結束
    expect(job.progress.elapsedSec).toBeCloseTo(1.7, 2)
    expect(job.elapsedSec).toBeCloseTo(1.7, 2)

    vi.advanceTimersByTime(2000) // t=4000，第 2 份：只算這份的 2 秒
    expect(job.progress.variantIndex).toBe(2)
    expect(job.progress.elapsedSec).toBeCloseTo(2, 2)
    expect(job.elapsedSec).toBeCloseTo(3.7, 2)

    vi.advanceTimersByTime(2000) // t=6000，第 3 份
    expect(job.progress.variantIndex).toBe(3)
    expect(job.progress.elapsedSec).toBeCloseTo(2, 2)
    expect(job.elapsedSec).toBeCloseTo(5.7, 2)
  })

  it('終態：GET 回的 progress.elapsedSec 是整個工作的總耗時（對齊 TerminalSnapshot）', () => {
    const job = startJob()
    vi.advanceTimersByTime(6500) // finish
    expect(job.status).toBe('succeeded')
    const res = toSolverJobResponse(job)
    expect(res.elapsedSec).toBeCloseTo(6.2, 2)
    expect(res.progress?.elapsedSec).toBeCloseTo(6.2, 2)
  })

  it('中止後 GET 回總耗時，不是中止前最後一筆的本份耗時', () => {
    const job = startJob()
    vi.advanceTimersByTime(4500) // 第 3 份進行中；最後一筆事件在 t=4000
    cancelSolverJob(job)
    const res = toSolverJobResponse(job)
    expect(res.status).toBe('cancelled')
    expect(res.elapsedSec).toBeCloseTo(4.2, 2)
    expect(res.progress?.elapsedSec).toBeCloseTo(4.2, 2)
  })

  it('stream：一開始訂閱就已結束送總耗時；中途轉終態最後一筆送本份耗時', () => {
    const job = startJob()
    vi.advanceTimersByTime(6500)
    expect(job.status).toBe('succeeded')
    // 中途訂閱者看到的最後一筆（非第一筆）是 live 快照：本份耗時
    expect(streamPayload(job, false).elapsedSec).toBeCloseTo(2.5, 2)
    // 一開始就已結束：TerminalSnapshot，總耗時
    expect(streamPayload(job, true).elapsedSec).toBeCloseTo(6.2, 2)
  })

  it('中止：variantIndex 與搜尋統計是最後一份完成的那份；沒有完成份時是 0／null', () => {
    const early = startJob()
    vi.advanceTimersByTime(1000)
    cancelSolverJob(early)
    expect(early.progress).toMatchObject({
      status: 'cancelled',
      variantIndex: 0,
      solutionCount: 0,
      bestObjective: null,
      bestBound: null,
      gap: null,
    })

    store.solverJobs.clear()
    const late = startJob()
    vi.advanceTimersByTime(4500) // 第 2 份在 t=4000 完成
    cancelSolverJob(late)
    expect(late.progress).toMatchObject({ status: 'cancelled', variantIndex: 2, solutionCount: 3 })
    expect(late.progress.bestObjective).toBe(90)
  })
})
