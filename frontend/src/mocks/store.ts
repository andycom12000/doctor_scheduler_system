/**
 * MSW mock 的全部可變狀態。單例、記憶體內、頁面重整或呼叫 `resetStore()` 才清空——
 * 這對應到真正後端「單機、單一 process」的行為，讓 mock 模式下的操作序列
 * （PATCH duties → validate → publish → …）跟真的打後端一樣有累積效果。
 */
import type {
  Area,
  AreaType,
  BlockedDayEntry,
  CalendarDayOverride,
  CarryOverEntry,
  ConstraintSettings,
  EligibilityMatrix,
  MonthlyOverride,
  PointRules,
  Rank,
  RankGroup,
  SolverJobStatus,
  SolverProgress,
  Staff,
  Variant,
} from '@/api/types'
import { areas as areaFixture, areaTypes as areaTypeFixture } from './fixtures/areas'
import { constraintSettings } from './fixtures/constraints'
import { pointRules as pointRulesFixture } from './fixtures/pointRules'
import { eligibilityMatrix as eligibilityMatrixFixture, rankGroups, ranks } from './fixtures/ranks'
import { makeStaffFixture } from './fixtures/staff'
import { buildSeedSchedules } from './fixtures/seed'
import type { ScheduleState } from './domain'

export type { ScheduleState }

export interface SolverJobState {
  jobId: string
  yearMonth: string
  status: SolverJobStatus
  variantCount: number
  elapsedSec: number
  scale: { staff: number; areas: number; days: number; variables: number }
  constraintCount: { hard: number; soft: number }
  progress: SolverProgress
  warnings: string[]
  failureReason: string | null
  /** 內部排程用，不出現在 API 回應裡。 */
  timeLimitSecPerVariant: number
  startedAtMs: number
}

export interface MockStore {
  areaTypes: AreaType[]
  areas: Area[]
  ranks: Rank[]
  rankGroups: RankGroup[]
  eligibilityMatrix: EligibilityMatrix
  pointRules: PointRules
  constraints: ConstraintSettings
  monthlyOverrides: Map<string, MonthlyOverride>
  /** key: 日期 `YYYY-MM-DD` */
  calendarOverrides: Map<string, CalendarDayOverride>
  staff: Staff[]
  nextStaffSeq: number
  /** key: 年月 `YYYY-MM` */
  schedules: Map<string, ScheduleState>
  /** key: 年月，登記獨立於值班表存在（ADR-0001） */
  blockedDays: Map<string, BlockedDayEntry[]>
  /** key: 生效的年月（發布 ym 的「下一個月」），值為該月結轉 */
  carryOver: Map<string, CarryOverEntry[]>
  solverJobs: Map<string, SolverJobState>
  /** key: jobId */
  variants: Map<string, Variant[]>
  nextJobSeq: number
}

export interface StoreOptions {
  /**
   * 0 人情境（issue #81）：名冊清空，連帶不種值班表種子（種子值班表引用的人員不存在）。
   * 其餘設定維持出廠值，這樣才分得出「沒人」與「沒設定」。
   */
  noStaff?: boolean
}

function buildInitialStore(options: StoreOptions = {}): MockStore {
  // 矩陣先算好、給 eligibilityMatrix 與 makeStaffFixture 共用同一份——
  // 兩者必須從同一個來源推導，PUT 矩陣之後才不會各吃各的（見 ranks.ts 的 eligibleAreaTypesOf 註解）。
  const matrix = structuredClone(eligibilityMatrixFixture.matrix)

  const store: MockStore = {
    areaTypes: areaTypeFixture.map((a) => ({ ...a })),
    areas: areaFixture.map((a) => ({ ...a })),
    ranks: ranks.map((r) => ({ ...r })),
    rankGroups: rankGroups.map((g) => ({ ...g })),
    eligibilityMatrix: { matrix },
    pointRules: structuredClone(pointRulesFixture),
    constraints: structuredClone(constraintSettings),
    monthlyOverrides: new Map(),
    calendarOverrides: new Map(),
    staff: options.noStaff ? [] : makeStaffFixture(matrix),
    nextStaffSeq: 35,
    schedules: new Map(),
    blockedDays: new Map(),
    carryOver: new Map(),
    solverJobs: new Map(),
    variants: new Map(),
    nextJobSeq: 1,
  }

  if (!options.noStaff) buildSeedSchedules(store)
  return store
}

export let store: MockStore = buildInitialStore()

/** 重置全部 mock 狀態，供 `mock:smoke` 腳本在每次執行前呼叫。 */
export function resetStore(options: StoreOptions = {}): void {
  store = buildInitialStore(options)
}
