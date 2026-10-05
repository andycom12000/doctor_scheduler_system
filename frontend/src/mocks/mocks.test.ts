import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computeVariantMetrics, dutyKey } from './domain'
import { scheduleSolverJob } from './handlers'
import { resetStore, store, type SolverJobState } from './store'

beforeEach(() => resetStore())

describe('computeVariantMetrics.fairnessPoint', () => {
  it('各身分組內 max − min 再加總，不是全體加總（對齊 Domain ScheduleScores.FairnessByGroup）', () => {
    const ym = '2026-03'
    // 同一身分組內挑兩位在職者：A 排兩天、B 排一天，都是平日接平日（週一、週二）。
    const groupOf = (code: string) => store.ranks.find((r) => r.code === code)!.groupCode
    const active = store.staff.filter((s) => s.status === 'active' && s.rankCode !== 'NP')
    const a = active[0]!
    const b = active.find((s) => s.id !== a.id && groupOf(s.rankCode) === groupOf(a.rankCode))!
    const c = active.find((s) => groupOf(s.rankCode) !== groupOf(a.rankCode))!
    const pointType = store.ranks.find((r) => r.code === a.rankCode)!.pointType!
    const weekdayPoints = store.pointRules.fairness.tables[pointType]!.find(
      (r) => r.today === 'weekday' && r.tomorrow === 'weekday',
    )!.points
    expect(weekdayPoints).toBeGreaterThan(0)

    const area = store.areas[0]!.id
    const duty = new Map([
      [dutyKey(area, '2026-03-02'), a.id],
      [dutyKey(area, '2026-03-03'), a.id],
      [dutyKey(area, '2026-03-04'), b.id],
    ])
    // A 組內其他人為 0：max − min = 2p − 0（舊算法全體加總會得 3p）。
    expect(computeVariantMetrics(store, ym, duty).fairnessPoint).toBe(2 * weekdayPoints)

    // 另一組的 C 也值一天：該組其他人為 0，max − min = 他自己的點數，兩組加總。
    duty.set(dutyKey(area, '2026-03-05'), c.id)
    const cType = store.ranks.find((r) => r.code === c.rankCode)!.pointType!
    const cPoints = store.pointRules.fairness.tables[cType]!.find(
      (r) => r.today === 'weekday' && r.tomorrow === 'weekday',
    )!.points
    expect(computeVariantMetrics(store, ym, duty).fairnessPoint).toBe(2 * weekdayPoints + cPoints)
  })
})

describe('scheduleSolverJob progress.elapsedSec', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('換份歸零：本份耗時不累計，job.elapsedSec 維持總耗時', () => {
    const job: SolverJobState = {
      jobId: 'job-t',
      yearMonth: '2026-03',
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

    vi.advanceTimersByTime(300)
    expect(job.progress.variantIndex).toBe(1)
    expect(job.progress.elapsedSec).toBe(0)

    vi.advanceTimersByTime(1700) // t=2000，第 1 份結束
    expect(job.progress.variantIndex).toBe(1)
    expect(job.progress.elapsedSec).toBeCloseTo(1.7, 2)
    expect(job.elapsedSec).toBeCloseTo(2, 2)

    vi.advanceTimersByTime(2000) // t=4000，第 2 份：只算這份的 2 秒
    expect(job.progress.variantIndex).toBe(2)
    expect(job.progress.elapsedSec).toBeCloseTo(2, 2)
    expect(job.elapsedSec).toBeCloseTo(4, 2)

    vi.advanceTimersByTime(2000) // t=6000，第 3 份
    expect(job.progress.variantIndex).toBe(3)
    expect(job.progress.elapsedSec).toBeCloseTo(2, 2)
    expect(job.elapsedSec).toBeCloseTo(6, 2)
  })
})
