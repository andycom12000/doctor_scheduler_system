/**
 * mock 端的推導邏輯：格子索引、兩套點數、違規檢查（簡化版）、月結轉、
 * 檢視（點數看板／單日詳表／空缺／候選人／可行性）與貪婪排班演算法。
 *
 * 這裡刻意**只 `import type`** `./store` 的 `MockStore`——所有函式都吃 `store`
 * 當參數，不 import 單例。這樣 `fixtures/seed.ts` 可以在建構初始 store 的過程中
 * 呼叫這裡的函式，而不會與 `store.ts` 形成執行期循環引用。
 */
import type {
  Candidate,
  CarryOverEntry,
  ConstraintScope,
  DayDetail,
  Duty,
  FeasibilityReport,
  HardConstraint,
  PointBoardGroup,
  Severity,
  SoftConstraint,
  Variant,
  VacancyByDate,
  ValidationResult,
  Violation,
} from '@/api/types'
import { areaFillOrder } from './fixtures/areas'
import {
  addDays,
  baseIsHoliday,
  calendarDayFacts,
  datesOfYearMonth,
  diffDays,
  nextDate,
  nextYearMonth,
  previousDate,
  previousYearMonth,
} from './fixtures/calendar'
import type { MockStore } from './store'
import type { Staff } from '@/api/types'

// ---------------------------------------------------------------------------
// 約束範圍（ConstraintScope）——NP 的四條特例與其他身分限定規則全部走這裡，
// 不在程式裡寫 `if (rankCode === 'NP')`（見 api-contract.yaml 的 ConstraintScope 說明）。
// ---------------------------------------------------------------------------

/** `rankCodes` 是命中清單（省略代表全體），`exemptRankCodes` 是豁免清單，兩者可同時存在。 */
export function appliesToRank(scope: ConstraintScope | undefined, rankCode: string): boolean {
  if (scope?.exemptRankCodes?.includes(rankCode)) return false
  if (scope?.rankCodes && !scope.rankCodes.includes(rankCode)) return false
  return true
}

function findHardConstraint(store: MockStore, code: string): HardConstraint | undefined {
  return store.constraints.hard.find((h) => h.code === code)
}

function findSoftConstraint(store: MockStore, code: string): SoftConstraint | undefined {
  return store.constraints.soft.find((s) => s.code === code)
}

/** S3/S4/S5/S6：目前唯一會產生逐格違規／影響產生器評分的四條 `Preference`（見 F10：S1/S2/S7 不算）。 */
const PREFERENCE_SOFT_CODES = ['S3_R2R3_PREFER_ICU', 'S4_R4R6_PREFER_CHIEF', 'S5_NP_LAST_RESORT', 'S6_NP_AVOID_HOLIDAY']

// ---------------------------------------------------------------------------
// 格子索引
// ---------------------------------------------------------------------------

export function dutyKey(areaId: string, date: string): string {
  return `${areaId}|${date}`
}

export function parseDutyKey(key: string): { areaId: string; date: string } {
  const separator = key.indexOf('|')
  return { areaId: key.slice(0, separator), date: key.slice(separator + 1) }
}

export function areaCellKey(areaId: string, date: string): string {
  return `area:${areaId}:${date}`
}

export function staffCellKey(staffId: string, date: string): string {
  return `staff:${staffId}:${date}`
}

// ---------------------------------------------------------------------------
// 值班表存取
// ---------------------------------------------------------------------------

export interface ScheduleState {
  yearMonth: string
  status: 'draft' | 'published'
  revision: number
  /** 定版版本號：只有發布才 +1，0 = 從未發布。 */
  publishedVersion: number
  /** 最近一次發布當下的 revision；跟 revision 不同就是「發布後有修改」（#75）。 */
  publishedRevision: number
  publishedAt: string | null
  /** key: `${areaId}|${date}`。未指派的格子不出現在這裡。 */
  duties: Map<string, string>
}

export function newScheduleState(yearMonth: string): ScheduleState {
  return { yearMonth, status: 'draft', revision: 0, publishedVersion: 0, publishedRevision: 0, publishedAt: null, duties: new Map() }
}

/** 該月尚無值班表時自動建立一份空草稿——`setDuty`／`swapDuties`／`applyVariant` 的入口語意。 */
export function ensureSchedule(store: MockStore, ym: string): ScheduleState {
  let schedule = store.schedules.get(ym)
  if (!schedule) {
    schedule = newScheduleState(ym)
    store.schedules.set(ym, schedule)
  }
  return schedule
}

export function scheduleToDuties(schedule: ScheduleState): Duty[] {
  return [...schedule.duties.entries()]
    .map(([key, staffId]) => {
      const { areaId, date } = parseDutyKey(key)
      return { areaId, date, staffId, cellKey: areaCellKey(areaId, date) }
    })
    .sort((a, b) => (a.date === b.date ? a.areaId.localeCompare(b.areaId) : a.date.localeCompare(b.date)))
}

// ---------------------------------------------------------------------------
// 行事曆 × 點數
// ---------------------------------------------------------------------------

export interface CalendarDayResult {
  date: string
  weekday: number
  isHoliday: boolean
  isPublicHoliday: boolean
  isMakeUpWorkday: boolean
  holidayName: string | null
  quotaPointValue: number
  overridden: boolean
}

export function getCalendarDay(store: MockStore, date: string): CalendarDayResult {
  const facts = calendarDayFacts(date)
  const override = store.calendarOverrides.get(date)
  const isPublicHoliday = override?.isPublicHoliday ?? facts.isPublicHoliday
  const isMakeUpWorkday = override?.isMakeUpWorkday ?? facts.isMakeUpWorkday
  const holidayName = override?.holidayName ?? facts.holidayName
  const isHoliday =
    override?.isHoliday ??
    (isMakeUpWorkday ? false : baseIsHoliday({ ...facts, isPublicHoliday, isMakeUpWorkday }))
  const quotaPointValue = isHoliday ? store.pointRules.quota.holiday : store.pointRules.quota.weekday
  return {
    date,
    weekday: facts.weekday,
    isHoliday,
    isPublicHoliday,
    isMakeUpWorkday,
    holidayName,
    quotaPointValue,
    overridden: Boolean(override),
  }
}

export function quotaPointValueOf(store: MockStore, date: string): number {
  return getCalendarDay(store, date).quotaPointValue
}

export function isHolidayDate(store: MockStore, date: string): boolean {
  return getCalendarDay(store, date).isHoliday
}

export function quotaCapFor(store: MockStore, rankCode: string, ym: string): number | null {
  const override = store.monthlyOverrides.get(ym)?.quotaCapByRank?.[rankCode]
  if (override !== undefined) return override
  return store.ranks.find((r) => r.code === rankCode)?.quotaCap ?? null
}

function dutyMapOf(store: MockStore, ym: string, override?: Map<string, string>): Map<string, string> {
  return override ?? store.schedules.get(ym)?.duties ?? new Map()
}

export function quotaPointsForStaffInMonth(
  store: MockStore,
  ym: string,
  staffId: string,
  dutyMapOverride?: Map<string, string>,
): number {
  let sum = 0
  for (const [key, sid] of dutyMapOf(store, ym, dutyMapOverride)) {
    if (sid !== staffId) continue
    sum += quotaPointValueOf(store, parseDutyKey(key).date)
  }
  return sum
}

export function holidayDutiesForStaffInMonth(
  store: MockStore,
  ym: string,
  staffId: string,
  dutyMapOverride?: Map<string, string>,
): number {
  let count = 0
  for (const [key, sid] of dutyMapOf(store, ym, dutyMapOverride)) {
    if (sid !== staffId) continue
    if (isHolidayDate(store, parseDutyKey(key).date)) count++
  }
  return count
}

export function dutyCountForStaffInMonth(
  store: MockStore,
  ym: string,
  staffId: string,
  dutyMapOverride?: Map<string, string>,
): number {
  let count = 0
  for (const [, sid] of dutyMapOf(store, ym, dutyMapOverride)) {
    if (sid === staffId) count++
  }
  return count
}

/** 該人在這個月（含跨月尾巴）所有值班日期，由舊到新排序，去重。 */
function staffDutyTimeline(
  store: MockStore,
  ym: string,
  staffId: string,
  dutyMapOverride?: Map<string, string>,
): string[] {
  const dates = new Set<string>()
  const prevSchedule = store.schedules.get(previousYearMonth(ym))
  if (prevSchedule) {
    for (const [key, sid] of prevSchedule.duties) {
      if (sid === staffId) dates.add(parseDutyKey(key).date)
    }
  }
  for (const [key, sid] of dutyMapOf(store, ym, dutyMapOverride)) {
    if (sid === staffId) dates.add(parseDutyKey(key).date)
  }
  return [...dates].sort()
}

/**
 * `staffId` 在 `date` 這天是否有值班——查任何月份的值班表，不限定 `ym`。
 * 連值週六 bonus 要看「7 天後」，那天可能落在下個月。
 *
 * **跨月是 best-effort**：只查 `store.schedules` 裡已經存在的月份；下個月的值班表
 * 若還沒產生（mock 常見情境），就查不到、bonus 就不會算給這個月——這一點刻意不強求
 * （F6：8 月最後一個週六 + 9 月第一個週六的情形，8 月結算時 9 月資料通常還沒有）。
 */
function hasDutyOnDate(store: MockStore, ym: string, staffId: string, date: string, dutyMapOverride?: Map<string, string>): boolean {
  const dateYm = date.slice(0, 7)
  const map = dateYm === ym ? dutyMapOf(store, ym, dutyMapOverride) : store.schedules.get(dateYm)?.duties
  if (!map) return false
  for (const [key, sid] of map) {
    if (sid === staffId && parseDutyKey(key).date === date) return true
  }
  return false
}

/** 自 `date` 起（含當日）`windowDays` 天內有沒有國定假日可以喘息。 */
function hasPublicHolidayInWindow(store: MockStore, date: string, windowDays: number): boolean {
  for (let i = 0; i < windowDays; i++) {
    if (getCalendarDay(store, addDays(date, i)).isPublicHoliday) return true
  }
  return false
}

/**
 * 公平性點數（實驗性）：查表 + 連值兩個週六加分。NP 不計，回 null。
 *
 * 連值週六 bonus 的語義（對齊 `MetricEvaluator.FairnessPointOf`）：**當日是週六**、
 * **7 天後同一人也有值班**、且**自當日起 `windowDays` 天內（含當日）沒有國定假日**
 * 可以喘息——bonus 記在「較早」的那個週六，不是兩個週六之間的間隔判斷。
 */
export function fairnessPointsForStaffInMonth(
  store: MockStore,
  ym: string,
  staffId: string,
  dutyMapOverride?: Map<string, string>,
): number | null {
  const staff = store.staff.find((s) => s.id === staffId)
  const rank = staff && store.ranks.find((r) => r.code === staff.rankCode)
  const pointType = rank?.pointType
  if (!staff || !pointType) return null

  const table = store.pointRules.fairness.tables[pointType] ?? []
  const bonus = store.pointRules.fairness.consecutiveSaturdayBonus
  const dutyDates = [...dutyMapOf(store, ym, dutyMapOverride).entries()]
    .filter(([, sid]) => sid === staffId)
    .map(([key]) => parseDutyKey(key).date)
    .sort()

  let total = 0
  for (const date of dutyDates) {
    const today = isHolidayDate(store, date) ? 'holiday' : 'weekday'
    const tomorrow = isHolidayDate(store, nextDate(date)) ? 'holiday' : 'weekday'
    const row = table.find((r) => r.today === today && r.tomorrow === tomorrow)
    total += row?.points ?? 0

    if (
      getCalendarDay(store, date).weekday === 6 &&
      hasDutyOnDate(store, ym, staffId, addDays(date, 7), dutyMapOverride) &&
      !hasPublicHolidayInWindow(store, date, bonus.windowDays)
    ) {
      total += bonus.points
    }
  }
  return total
}

// ---------------------------------------------------------------------------
// 確定性違規 id（FNV-1a）
// ---------------------------------------------------------------------------

function fnv1a(input: string): string {
  let hash = 0x811c_9dc5
  for (let i = 0; i < input.length; i++) {
    hash ^= input.charCodeAt(i)
    hash = Math.imul(hash, 0x0100_0193)
  }
  return (hash >>> 0).toString(16)
}

export function violationId(code: string, cellKeys: string[]): string {
  return fnv1a(`${code}|${[...cellKeys].sort().join(',')}`)
}

/** `2026-09-05` → `9/5`，對齊後端訊息的 `M/d`。 */
function shortDate(date: string): string {
  return `${Number(date.slice(5, 7))}/${Number(date.slice(8, 10))}`
}

function makeViolation(code: string, severity: Severity, cellKeys: string[], message: string): Violation {
  return { id: violationId(code, cellKeys), code, severity, cellKeys, message }
}

function dayKindMatches(store: MockStore, date: string, kind: string): boolean {
  const day = getCalendarDay(store, date)
  if (kind === 'holiday') return day.isHoliday
  if (kind === 'publicHoliday') return day.isPublicHoliday
  return !day.isHoliday
}

function metricName(metric: string): string {
  if (metric === 'quota_point') return '額度點數'
  if (metric === 'fairness_point') return '公平性點數'
  if (metric === 'duty_day') return '值班天數'
  return metric
}

/** 每一格在該度量下的值：`quota_point` 看行事曆，`duty_day` 固定 1（F4）。 */
function budgetMetricValue(store: MockStore, metric: string, date: string): number {
  if (metric === 'quota_point') return quotaPointValueOf(store, date)
  if (metric === 'duty_day') return 1
  return 0
}

/**
 * `Budget` 原語的共用實作：H3（額度點數上限）與 H6（NP 每月天數上限）都是它，
 * 差別只在 `metric` 與 `scope`——對齊 `ViolationChecker.Budget`。
 *
 * 逐格按日期排序累加，**只標「累計超出上限」那幾格**（F5），不是整個人整段格子；
 * `params.cap` 沒填時，只有 `quota_point` 能從 `Rank.quotaCap`／當月覆寫推出上限，
 * 其餘度量沒有 cap 就跳過（讀出 `null` 代表不計，不是 0）。
 */
function checkBudget(store: MockStore, ym: string, dutyMap: Map<string, string>, constraint: HardConstraint): Violation[] {
  const metric = constraint.metric
  if (!metric) return []
  const violations: Violation[] = []

  for (const staff of store.staff) {
    if (!appliesToRank(constraint.scope, staff.rankCode)) continue
    const paramsCap = (constraint.params as { cap?: number } | undefined)?.cap
    const cap = paramsCap ?? (metric === 'quota_point' ? quotaCapFor(store, staff.rankCode, ym) : null)
    if (cap === null || cap === undefined) continue

    const cells = [...dutyMap.entries()]
      .filter(([, sid]) => sid === staff.id)
      .map(([key]) => parseDutyKey(key))
      .sort((a, b) => a.date.localeCompare(b.date))

    let running = 0
    const excess: string[] = []
    for (const cell of cells) {
      running += budgetMetricValue(store, metric, cell.date)
      if (running > cap) excess.push(staffCellKey(staff.id, cell.date))
    }

    if (excess.length > 0) {
      violations.push(
        makeViolation(
          constraint.code,
          'hard',
          excess,
          `${staff.name}（${staff.rankCode}）本月${metricName(metric)} ${running} 超過上限 ${cap}`,
        ),
      )
    }
  }
  return violations
}

/**
 * 簡化版違規檢查。硬約束 H1–H7 全部做；軟約束**只做 S3/S4/S5/S6 的 `Preference`**
 * 逐格違規。`S1`/`S7`（`Fairness`）與 `S2`（`Consistency`）依任務指示不產生逐格違規，
 * 數值只出現在點數看板與變體指標——這點與 api-contract.yaml 對 `Consistency` 的敘述
 * 不完全一致，是刻意的簡化，詳見 PR 說明。
 */
export function computeViolationsForDuties(
  store: MockStore,
  ym: string,
  dutyMap: Map<string, string>,
): Violation[] {
  const violations: Violation[] = []
  const dates = datesOfYearMonth(ym)
  const hardByCode = new Map(store.constraints.hard.map((h) => [h.code, h]))
  const softByCode = new Map(store.constraints.soft.map((s) => [s.code, s]))

  // H1 每日每區恰好 1 人
  if (hardByCode.get('H1_AREA_COVERAGE')?.enabled) {
    for (const date of dates) {
      for (const area of store.areas) {
        if (!dutyMap.has(dutyKey(area.id, date))) {
          violations.push(
            makeViolation('H1_AREA_COVERAGE', 'hard', [areaCellKey(area.id, date)], `${date} ${area.name} 未指派人員`),
          )
        }
      }
    }
  }

  // H2 身分資格
  if (hardByCode.get('H2_ELIGIBILITY')?.enabled) {
    for (const [key, staffId] of dutyMap) {
      const { areaId, date } = parseDutyKey(key)
      const staff = store.staff.find((s) => s.id === staffId)
      const area = store.areas.find((a) => a.id === areaId)
      if (!staff || !area) continue
      if (!store.eligibilityMatrix.matrix[staff.rankCode]?.[area.areaTypeCode]) {
        violations.push(
          makeViolation(
            'H2_ELIGIBILITY',
            'hard',
            [areaCellKey(areaId, date)],
            `${staff.name}（${staff.rankCode}）不具備 ${area.name} 資格`,
          ),
        )
      }
    }
  }

  // H3 額度點數上限（Budget，metric=quota_point）。scope.exemptRankCodes 豁免 NP，
  // 只標「累計超出上限」那幾格（F5），不是整段格子。
  const h3 = hardByCode.get('H3_QUOTA_CAP')
  if (h3?.enabled) {
    violations.push(...checkBudget(store, ym, dutyMap, h3))
  }

  // H4 值休休（MinGap，scope 決定適用對象，跨月讀上月尾巴）
  const h4 = hardByCode.get('H4_MIN_GAP')
  if (h4?.enabled) {
    const gapDays = (h4.params as { days?: number } | undefined)?.days ?? 3
    for (const staff of store.staff) {
      if (!appliesToRank(h4.scope, staff.rankCode)) continue
      const timeline = staffDutyTimeline(store, ym, staff.id, dutyMap)
      for (let i = 1; i < timeline.length; i++) {
        if (diffDays(timeline[i], timeline[i - 1]) < gapDays) {
          const cellKeys = [timeline[i - 1], timeline[i]]
            .filter((d) => d.startsWith(ym))
            .map((d) => staffCellKey(staff.id, d))
          if (cellKeys.length === 0) continue
          violations.push(
            makeViolation(
              'H4_MIN_GAP',
              'hard',
              cellKeys,
              `${staff.name} 值班間隔不足 ${gapDays} 天（${timeline[i - 1]} → ${timeline[i]}）`,
            ),
          )
        }
      }
    }
  }

  // H5 不可排班日
  if (hardByCode.get('H5_BLOCKED_DAY')?.enabled) {
    const blockedSet = new Set((store.blockedDays.get(ym) ?? []).map((e) => `${e.staffId}|${e.date}`))
    for (const [key, staffId] of dutyMap) {
      const { areaId, date } = parseDutyKey(key)
      if (blockedSet.has(`${staffId}|${date}`)) {
        const staff = store.staff.find((s) => s.id === staffId)
        violations.push(
          makeViolation(
            'H5_BLOCKED_DAY',
            'hard',
            [areaCellKey(areaId, date)],
            `${staff?.name ?? staffId} 於不可排班日被排班（${date}）`,
          ),
        )
      }
    }
  }

  // H6 每月值班天數上限（Budget，metric=duty_day，逐格累加——F4：不是相異日期數）。
  // scope.rankCodes:[NP] 決定適用對象，不寫 if (rankCode === 'NP')。
  const h6 = hardByCode.get('H6_NP_MONTHLY_DAYS')
  if (h6?.enabled) {
    violations.push(...checkBudget(store, ym, dutyMap, h6))
  }

  // H7 最多連續值班天數（MaxConsecutive，scope 決定適用對象，跨月）。
  // 只標超出上限的那幾格（第 limit+1 格起，F5），且只標本月的。
  const h7 = hardByCode.get('H7_NP_MAX_CONSECUTIVE')
  if (h7?.enabled) {
    const maxDays = (h7.params as { days?: number } | undefined)?.days ?? 6
    for (const staff of store.staff) {
      if (!appliesToRank(h7.scope, staff.rankCode)) continue
      const timeline = staffDutyTimeline(store, ym, staff.id, dutyMap)
      let runStart = 0
      for (let i = 1; i <= timeline.length; i++) {
        const broke = i === timeline.length || diffDays(timeline[i], timeline[i - 1]) !== 1
        if (broke) {
          const runLength = i - runStart
          if (runLength > maxDays) {
            const run = timeline.slice(runStart, i)
            const excess = run.slice(maxDays).filter((d) => d.startsWith(ym))
            if (excess.length > 0) {
              violations.push(
                makeViolation(
                  'H7_NP_MAX_CONSECUTIVE',
                  'hard',
                  excess.map((d) => staffCellKey(staff.id, d)),
                  `${staff.name} 自 ${run[0]} 起連續值班 ${runLength} 天，超過上限 ${maxDays} 天`,
                ),
              )
            }
          }
          runStart = i
        }
      }
    }
  }

  // S3/S4/S5/S6：Preference 逐格違規
  for (const code of PREFERENCE_SOFT_CODES) {
    const constraint = softByCode.get(code)
    if (!constraint || constraint.weight <= 0) continue
    const scope = constraint.scope ?? {}
    const direction = (constraint.params as { direction?: string } | undefined)?.direction

    for (const [key, staffId] of dutyMap) {
      const { areaId, date } = parseDutyKey(key)
      const staff = store.staff.find((s) => s.id === staffId)
      const area = store.areas.find((a) => a.id === areaId)
      if (!staff || !area) continue
      if (!appliesToRank(constraint.scope, staff.rankCode)) continue

      if (direction === 'prefer') {
        if (scope.areaTypeCodes && !scope.areaTypeCodes.includes(area.areaTypeCode)) {
          violations.push(
            makeViolation(
              code,
              'soft',
              [areaCellKey(areaId, date)],
              `${staff.name}（${staff.rankCode}）值 ${area.name}，未依偏好優先 ${scope.areaTypeCodes.join('/')}`,
            ),
          )
        }
      } else if (direction === 'avoid') {
        const areaTypeMatch = !scope.areaTypeCodes || scope.areaTypeCodes.includes(area.areaTypeCode)
        const dayKindMatch = !scope.dayKinds || scope.dayKinds.some((k) => dayKindMatches(store, date, k))
        if (areaTypeMatch && dayKindMatch) {
          violations.push(
            makeViolation(code, 'soft', [areaCellKey(areaId, date)], `${staff.name} 被排入應盡量避開的班（${date} ${area.name}）`),
          )
        }
      }
    }
  }

  // X1 結構規則（不是原語、不看約束設定、不能停用）：同一人同一天排在兩區以上。
  // 一個（人，日）一筆，cellKeys 含他當天所在的每個 area 格，對齊 ViolationChecker.StaffDoubleBooked。
  const areasByStaffDate = new Map<string, { staffId: string; date: string; areaIds: string[] }>()
  for (const [key, staffId] of dutyMap) {
    const { areaId, date } = parseDutyKey(key)
    const groupKey = `${staffId}|${date}`
    const entry = areasByStaffDate.get(groupKey) ?? { staffId, date, areaIds: [] }
    entry.areaIds.push(areaId)
    areasByStaffDate.set(groupKey, entry)
  }
  for (const { staffId, date, areaIds } of areasByStaffDate.values()) {
    if (areaIds.length < 2) continue
    const staff = store.staff.find((s) => s.id === staffId)
    const names = areaIds.map((id) => store.areas.find((a) => a.id === id)?.name ?? id)
    violations.push(
      makeViolation(
        'X1_STAFF_DOUBLE_BOOKED',
        'hard',
        areaIds.map((id) => areaCellKey(id, date)),
        `${staff?.name ?? staffId} ${shortDate(date)} 同時排在 ${names.join('、')}`,
      ),
    )
  }

  return violations
}

export function computeViolations(store: MockStore, ym: string): Violation[] {
  return computeViolationsForDuties(store, ym, dutyMapOf(store, ym))
}

export function computeValidationResult(store: MockStore, ym: string): ValidationResult {
  const violations = computeViolations(store, ym)
  const hard = violations.filter((v) => v.severity === 'hard').length
  const soft = violations.filter((v) => v.severity === 'soft').length
  return { ok: hard === 0, violations, summary: { hard, soft } }
}

// ---------------------------------------------------------------------------
// 月結轉
// ---------------------------------------------------------------------------

/**
 * `Fairness(quota_point)` 真正比較的量——`cap − 已排 − 月結轉偏移`（對齊
 * `MetricEvaluator.QuotaRemaining`）。**不是** `PointBoardRow.quotaRemaining`：
 * 那個欄位契約明定就是 `quotaCap − quotaPoints`，是給排班者看的原始數字，
 * 不能把月結轉偏移混進去（會跟契約對不上）。這裡是給「公平性比較」與
 * 「下個月結轉的結算」兩處內部使用的度量，两者必须用同一個定義（ADR-0002）。
 */
function quotaRemainingForFairness(
  store: MockStore,
  ym: string,
  staffId: string,
  dutyMapOverride?: Map<string, string>,
): number | null {
  const staff = store.staff.find((s) => s.id === staffId)
  if (!staff) return null
  const cap = quotaCapFor(store, staff.rankCode, ym)
  if (cap === null) return null
  const points = quotaPointsForStaffInMonth(store, ym, staffId, dutyMapOverride)
  const carryIn = (store.carryOver.get(ym) ?? []).find((e) => e.staffId === staffId)?.points ?? 0
  return cap - points - carryIn
}

/** `組內最大剩餘額度 − 本人剩餘額度`，發布時結算，供下個月使用。NP 不參與。 */
export function computeCarryOverEntries(store: MockStore, ym: string): CarryOverEntry[] {
  const entries: CarryOverEntry[] = []
  for (const group of store.rankGroups) {
    if (group.code === 'NP') continue
    const members = store.staff.filter(
      (s) => s.status === 'active' && store.ranks.find((r) => r.code === s.rankCode)?.groupCode === group.code,
    )
    if (members.length === 0) continue
    const remainders = members
      .map((s) => ({ staffId: s.id, remaining: quotaRemainingForFairness(store, ym, s.id) }))
      .filter((r): r is { staffId: string; remaining: number } => r.remaining !== null)
    if (remainders.length === 0) continue
    const maxRemaining = Math.max(...remainders.map((r) => r.remaining))
    for (const r of remainders) {
      entries.push({ staffId: r.staffId, points: Math.max(0, maxRemaining - r.remaining) })
    }
  }
  return entries
}

// ---------------------------------------------------------------------------
// 檢視
// ---------------------------------------------------------------------------

export function computePointBoard(store: MockStore, ym: string): PointBoardGroup[] {
  const carryMap = new Map((store.carryOver.get(ym) ?? []).map((e) => [e.staffId, e.points]))
  const groups: PointBoardGroup[] = store.rankGroups.map((g) => ({ groupCode: g.code, groupName: g.name, rows: [] }))
  const groupByCode = new Map(groups.map((g) => [g.groupCode, g]))

  for (const staff of store.staff) {
    if (staff.status !== 'active') continue
    const rank = store.ranks.find((r) => r.code === staff.rankCode)
    const group = rank && groupByCode.get(rank.groupCode)
    if (!rank || !group) continue

    const quotaPoints = quotaPointsForStaffInMonth(store, ym, staff.id)
    const quotaCap = quotaCapFor(store, staff.rankCode, ym)
    group.rows.push({
      staffId: staff.id,
      name: staff.name,
      rankCode: staff.rankCode,
      quotaPoints,
      quotaCap,
      quotaRemaining: quotaCap === null ? null : quotaCap - quotaPoints,
      carryOverApplied: carryMap.get(staff.id) ?? 0,
      fairnessPoints: fairnessPointsForStaffInMonth(store, ym, staff.id),
      duties: dutyCountForStaffInMonth(store, ym, staff.id),
      holidayDuties: holidayDutiesForStaffInMonth(store, ym, staff.id),
    })
  }
  return groups.filter((g) => g.rows.length > 0)
}

export function computeDayDetail(store: MockStore, ym: string, date: string): DayDetail {
  const day = getCalendarDay(store, date)
  const schedule = store.schedules.get(ym)
  const areasOut = store.areas.map((area) => {
    const staffId = schedule?.duties.get(dutyKey(area.id, date))
    const staff = staffId ? store.staff.find((s) => s.id === staffId) : undefined
    return {
      areaId: area.id,
      code: area.code,
      areaTypeCode: area.areaTypeCode,
      filled: Boolean(staff),
      staff: staff
        ? {
            staffId: staff.id,
            name: staff.name,
            rankCode: staff.rankCode,
            monthQuotaPoints: quotaPointsForStaffInMonth(store, ym, staff.id),
            monthQuotaCap: quotaCapFor(store, staff.rankCode, ym),
            monthHolidayDuties: holidayDutiesForStaffInMonth(store, ym, staff.id),
          }
        : null,
    }
  })
  return {
    date,
    isHoliday: day.isHoliday,
    isPublicHoliday: day.isPublicHoliday,
    quotaPointValue: day.quotaPointValue,
    areas: areasOut,
  }
}

export function computeVacancies(store: MockStore, ym: string): { total: number; byDate: VacancyByDate[] } {
  const schedule = store.schedules.get(ym)
  const byDate: VacancyByDate[] = []
  let total = 0
  for (const date of datesOfYearMonth(ym)) {
    const areaIds = store.areas.filter((a) => !schedule?.duties.has(dutyKey(a.id, date))).map((a) => a.id)
    if (areaIds.length > 0) {
      byDate.push({ date, areaIds, count: areaIds.length })
      total += areaIds.length
    }
  }
  return { total, byDate }
}

export function computeCandidates(store: MockStore, ym: string, areaId: string, date: string): Candidate[] {
  const area = store.areas.find((a) => a.id === areaId)
  if (!area) return []

  const blockedToday = new Set(
    (store.blockedDays.get(ym) ?? []).filter((e) => e.date === date).map((e) => e.staffId),
  )
  const schedule = store.schedules.get(ym)
  // 當天已在其他區的人 → 那些區的 areaId（同人同日兩區 X1，#68）
  const elsewhereToday = new Map<string, string[]>()
  if (schedule) {
    for (const [key, sid] of schedule.duties) {
      const parsed = parseDutyKey(key)
      if (parsed.date === date && parsed.areaId !== areaId) {
        elsewhereToday.set(sid, [...(elsewhereToday.get(sid) ?? []), parsed.areaId])
      }
    }
  }

  // 候選人的阻擋理由讀 H3/H4/H6 的 scope 與 params，不寫死 rankCode（F7）。
  const h3 = findHardConstraint(store, 'H3_QUOTA_CAP')
  const h4 = findHardConstraint(store, 'H4_MIN_GAP')
  const h6 = findHardConstraint(store, 'H6_NP_MONTHLY_DAYS')

  const out: Candidate[] = []
  for (const staff of store.staff) {
    if (staff.status !== 'active') continue
    if (!staff.eligibleAreaTypes.includes(area.areaTypeCode)) continue

    const blockingReasons: string[] = []
    const warnings: string[] = []
    if (blockedToday.has(staff.id)) blockingReasons.push('該日已登記不可排班')
    const elsewhere = elsewhereToday.get(staff.id)
    if (elsewhere) {
      // 對齊後端 X1 訊息：把候選的這一區和他已在的區一起列出，依名稱排序
      const names = [area.name, ...elsewhere.map((id) => store.areas.find((a) => a.id === id)?.name ?? id)].sort()
      blockingReasons.push(`${staff.name} ${shortDate(date)} 同時排在 ${names.join('、')}`)
    }

    const cap = quotaCapFor(store, staff.rankCode, ym)
    const points = quotaPointsForStaffInMonth(store, ym, staff.id)

    if (h4?.enabled && appliesToRank(h4.scope, staff.rankCode)) {
      const gapDays = (h4.params as { days?: number } | undefined)?.days ?? 3
      const timeline = staffDutyTimeline(store, ym, staff.id).filter((d) => d !== date)
      if (timeline.some((d) => Math.abs(diffDays(date, d)) < gapDays)) {
        blockingReasons.push(`值休休間隔不足 ${gapDays} 天`)
      }
    }
    if (h3?.enabled && appliesToRank(h3.scope, staff.rankCode)) {
      if (cap !== null && points + quotaPointValueOf(store, date) > cap) {
        blockingReasons.push('超過額度點數上限')
      }
    }
    if (h6?.enabled && appliesToRank(h6.scope, staff.rankCode)) {
      const dayCap = (h6.params as { cap?: number } | undefined)?.cap ?? 20
      const scopedDates = new Set(staffDutyTimeline(store, ym, staff.id).filter((d) => d.startsWith(ym)))
      if (scopedDates.size >= dayCap) blockingReasons.push(`本月值班天數已達上限 ${dayCap} 天`)
      warnings.push('屬於後備人力範圍，優先考慮一般身分')
    }

    const quotaRemaining = cap === null ? null : cap - points
    const totalDuties = dutyCountForStaffInMonth(store, ym, staff.id)
    const thisAreaDuties = schedule
      ? [...schedule.duties.entries()].filter(([key, sid]) => sid === staff.id && parseDutyKey(key).areaId === areaId)
          .length
      : 0
    out.push({
      staffId: staff.id,
      name: staff.name,
      rankCode: staff.rankCode,
      quotaRemaining,
      areaConsistency: totalDuties > 0 ? thisAreaDuties / totalDuties : 0,
      blockingReasons,
      warnings,
    })
  }

  out.sort((a, b) => {
    if (a.blockingReasons.length !== b.blockingReasons.length) {
      return a.blockingReasons.length - b.blockingReasons.length
    }
    return (b.quotaRemaining ?? -1) - (a.quotaRemaining ?? -1)
  })
  return out
}

export function previousMonthWarnings(store: MockStore, ym: string): string[] {
  const prevSchedule = store.schedules.get(previousYearMonth(ym))
  if (!prevSchedule || prevSchedule.status !== 'published') {
    return ['上月尚未發布，月結轉為空']
  }
  return []
}

const FEASIBILITY_TIERS: string[][] = [['CHIEF'], ['CHIEF', 'ICU'], ['CHIEF', 'ICU', 'WARD']]

export function computeFeasibility(store: MockStore, ym: string): FeasibilityReport {
  const dates = datesOfYearMonth(ym)
  const blockedByStaff = new Map<string, Set<string>>()
  for (const entry of store.blockedDays.get(ym) ?? []) {
    const set = blockedByStaff.get(entry.staffId) ?? new Set<string>()
    set.add(entry.date)
    blockedByStaff.set(entry.staffId, set)
  }

  const byDate = dates.map((date) => {
    const shortages: { areaTypeCode: string; required: number; availableStaff: number }[] = []
    for (const areaType of store.areaTypes) {
      const required = store.areas
        .filter((a) => a.areaTypeCode === areaType.code)
        .reduce((sum, a) => sum + a.requiredPerDay, 0)
      // byDate 不排除 NP：NP 有一般病房資格，逐日可用人數該把他算進去（F12）。
      // bySupply 才排除 NP——那是額度點數的巢狀累計，NP 沒有 quotaCap，見下方。
      const eligible = store.staff.filter(
        (s) => s.status === 'active' && s.eligibleAreaTypes.includes(areaType.code),
      )
      const availableStaff = eligible.filter((s) => !blockedByStaff.get(s.id)?.has(date)).length
      if (availableStaff < required) shortages.push({ areaTypeCode: areaType.code, required, availableStaff })
    }
    return { date, shortages }
  })

  const bySupply = FEASIBILITY_TIERS.map((areaTypeCodes) => {
    const demandPoints = dates.reduce((sum, date) => {
      const perDay = store.areas
        .filter((a) => areaTypeCodes.includes(a.areaTypeCode))
        .reduce((s, a) => s + a.requiredPerDay, 0)
      return sum + perDay * quotaPointValueOf(store, date)
    }, 0)

    const eligible = store.staff.filter(
      (s) => s.status === 'active' && s.rankCode !== 'NP' && s.eligibleAreaTypes.some((t) => areaTypeCodes.includes(t)),
    )
    let supplyPoints = 0
    for (const s of eligible) {
      const cap = quotaCapFor(store, s.rankCode, ym) ?? 0
      // 登記日不消耗額度：每人供給 = min(上限, 沒登記的日子的額度點數總和)。
      const blockedDates = blockedByStaff.get(s.id) ?? new Set<string>()
      const availablePoints = dates
        .filter((d) => !blockedDates.has(d))
        .reduce((sum, d) => sum + quotaPointValueOf(store, d), 0)
      supplyPoints += Math.min(cap, availablePoints)
    }
    return { areaTypeCodes, demandPoints, supplyPoints, headroom: supplyPoints - demandPoints }
  })

  return {
    feasible: bySupply.every((t) => t.headroom >= 0) && byDate.every((d) => d.shortages.length === 0),
    byDate,
    bySupply,
    warnings: previousMonthWarnings(store, ym),
  }
}

// ---------------------------------------------------------------------------
// 貪婪排班演算法：seed 與求解變體共用
// ---------------------------------------------------------------------------

function pseudoRandom(key: string): number {
  return Number.parseInt(fnv1a(key), 16) / 0xff_ff_ff_ff
}

export interface GenerateOptions {
  seedOffset: number
  /** 軟約束權重乘數，鍵為代碼，未列出者為 1（ADR-0003）。 */
  weightMultipliers?: Record<string, number>
}

/**
 * 簡化版貪婪排班：逐日、依 `areaFillOrder` 逐區指派，盡量滿足硬約束
 * （資格、額度、值休休、不可排班日、Budget／MaxConsecutive 的 scope 對象），
 * 用「store 裡的權重 × 乘數」+ 決定性雜湊排序在可行候選人裡挑一個。
 * 找不到可行候選人時該格留空（H1 由驗證層抓，這裡不擋寫入）。
 *
 * H3/H4/H6/H7 的適用對象一律讀 `store.constraints` 的 `scope`，不寫死
 * `rankCode === 'NP'`（F7）——H6/H7 因此對每一位落在 scope 內的人各自追蹤，
 * 不假設「只有一位 NP」。
 *
 * 只讀上個月（`previousYearMonth(ym)`）的值班尾巴當固定輸入，
 * **不讀 `ym` 本身現有的值班表**——變體是全新假設性排班，不是在既有草稿上疊加。
 */
export function generateAssignment(store: MockStore, ym: string, options: GenerateOptions): Map<string, string> {
  const dates = datesOfYearMonth(ym)
  const result = new Map<string, string>()
  const quotaUsed = new Map<string, number>()
  const lastDutyDate = new Map<string, string>()
  const areaCountByStaff = new Map<string, Map<string, number>>()
  const blockedByStaff = new Map<string, Set<string>>()
  for (const entry of store.blockedDays.get(ym) ?? []) {
    const set = blockedByStaff.get(entry.staffId) ?? new Set<string>()
    set.add(entry.date)
    blockedByStaff.set(entry.staffId, set)
  }
  const carryMap = new Map((store.carryOver.get(ym) ?? []).map((e) => [e.staffId, e.points]))

  const h3 = findHardConstraint(store, 'H3_QUOTA_CAP')
  const h4 = findHardConstraint(store, 'H4_MIN_GAP')
  const h6 = findHardConstraint(store, 'H6_NP_MONTHLY_DAYS')
  const h7 = findHardConstraint(store, 'H7_NP_MAX_CONSECUTIVE')
  const h6DayCap = (h6?.params as { cap?: number } | undefined)?.cap ?? 20
  const h7Limit = (h7?.params as { days?: number } | undefined)?.days ?? 6
  const gapDays = (h4?.params as { days?: number } | undefined)?.days ?? 3

  const prevSchedule = store.schedules.get(previousYearMonth(ym))
  if (prevSchedule) {
    for (const [key, staffId] of prevSchedule.duties) {
      const { date } = parseDutyKey(key)
      const current = lastDutyDate.get(staffId)
      if (!current || date > current) lastDutyDate.set(staffId, date)
    }
  }

  // H6（Budget，metric=duty_day）本月累計，只看 scope 內的人；H7（MaxConsecutive）
  // 的連續天數需要從上月尾巴接續，一樣只算 scope 內的人。
  const scopedDutyCount = new Map<string, number>()
  const streakByStaff = new Map<string, number>()
  if (h7 && prevSchedule) {
    const prevDates = datesOfYearMonth(previousYearMonth(ym))
    for (const staff of store.staff) {
      if (!appliesToRank(h7.scope, staff.rankCode)) continue
      const datesWorked = new Set(
        [...prevSchedule.duties.entries()].filter(([, sid]) => sid === staff.id).map(([key]) => parseDutyKey(key).date),
      )
      let streak = 0
      for (const d of [...prevDates].reverse()) {
        if (datesWorked.has(d)) streak++
        else break
      }
      streakByStaff.set(staff.id, streak)
    }
  }

  /** 有效權重 = store 設定的 weight × 變體乘數（F2）。停用／weight=0 的約束乘任何數仍是 0。 */
  const effectiveWeight = (code: string): number => {
    const constraint = findSoftConstraint(store, code)
    if (!constraint) return 0
    const multiplier = options.weightMultipliers?.[code] ?? 1
    return constraint.weight * multiplier
  }

  for (const date of dates) {
    const day = getCalendarDay(store, date)
    const assignedToday = new Set<string>()

    for (const areaId of areaFillOrder) {
      const area = store.areas.find((a) => a.id === areaId)
      if (!area) continue

      const feasible = store.staff.filter((s) => {
        if (s.status !== 'active') return false
        if (!s.eligibleAreaTypes.includes(area.areaTypeCode)) return false
        if (assignedToday.has(s.id)) return false
        if (blockedByStaff.get(s.id)?.has(date)) return false

        if (h4?.enabled && appliesToRank(h4.scope, s.rankCode)) {
          const last = lastDutyDate.get(s.id)
          if (last && diffDays(date, last) < gapDays) return false
        }
        if (h3?.enabled && appliesToRank(h3.scope, s.rankCode)) {
          const cap = quotaCapFor(store, s.rankCode, ym)
          const used = quotaUsed.get(s.id) ?? 0
          if (cap !== null && used + day.quotaPointValue > cap) return false
        }
        if (h6?.enabled && appliesToRank(h6.scope, s.rankCode)) {
          const used = scopedDutyCount.get(s.id) ?? 0
          if (used + 1 > h6DayCap) return false
        }
        if (h7?.enabled && appliesToRank(h7.scope, s.rankCode)) {
          const streak = streakByStaff.get(s.id) ?? 0
          if (streak + 1 > h7Limit) return false
        }
        return true
      })
      if (feasible.length === 0) continue

      let best: Staff | null = null
      let bestScore = Number.NEGATIVE_INFINITY
      for (const s of feasible) {
        let score = 0
        const cap = quotaCapFor(store, s.rankCode, ym)
        const used = quotaUsed.get(s.id) ?? 0
        const remaining = cap === null ? 0 : cap - used
        score += remaining * 2 * effectiveWeight('S1_QUOTA_FAIRNESS')
        score -= (carryMap.get(s.id) ?? 0) * 0.5

        const thisAreaCount = areaCountByStaff.get(s.id)?.get(areaId) ?? 0
        score += thisAreaCount * 3 * effectiveWeight('S2_AREA_CONSISTENCY')

        for (const code of PREFERENCE_SOFT_CODES) {
          const constraint = findSoftConstraint(store, code)
          if (!constraint || !appliesToRank(constraint.scope, s.rankCode)) continue
          const direction = (constraint.params as { direction?: string } | undefined)?.direction
          const areaMatch = !constraint.scope?.areaTypeCodes || constraint.scope.areaTypeCodes.includes(area.areaTypeCode)
          const dayMatch = !constraint.scope?.dayKinds || constraint.scope.dayKinds.some((k) => dayKindMatches(store, date, k))
          const targetMatch = areaMatch && dayMatch
          const w = effectiveWeight(code)
          if (direction === 'prefer') score += (targetMatch ? 1 : -1) * w
          else if (direction === 'avoid' && targetMatch) score -= w
        }

        score += pseudoRandom(`${options.seedOffset}:${date}:${areaId}:${s.id}`) * 1.5

        if (score > bestScore) {
          bestScore = score
          best = s
        }
      }
      if (!best) continue

      result.set(dutyKey(areaId, date), best.id)
      assignedToday.add(best.id)
      quotaUsed.set(best.id, (quotaUsed.get(best.id) ?? 0) + day.quotaPointValue)
      lastDutyDate.set(best.id, date)
      const areaCounts = areaCountByStaff.get(best.id) ?? new Map<string, number>()
      areaCounts.set(areaId, (areaCounts.get(areaId) ?? 0) + 1)
      areaCountByStaff.set(best.id, areaCounts)
      if (h6?.enabled && appliesToRank(h6.scope, best.rankCode)) {
        scopedDutyCount.set(best.id, (scopedDutyCount.get(best.id) ?? 0) + 1)
      }
    }

    if (h7?.enabled) {
      for (const staff of store.staff) {
        if (!appliesToRank(h7.scope, staff.rankCode)) continue
        const worked = assignedToday.has(staff.id)
        streakByStaff.set(staff.id, worked ? (streakByStaff.get(staff.id) ?? 0) + 1 : 0)
      }
    }
  }

  return result
}

function diffCellCount(a: Map<string, string>, b: Map<string, string>): number {
  const keys = new Set([...a.keys(), ...b.keys()])
  let diff = 0
  for (const key of keys) if (a.get(key) !== b.get(key)) diff++
  return diff
}

/**
 * 多樣性約束（ADR-0003）：讓 `candidate` 與 `reference` 至少差 `minDiff` 格。
 * 只在兩者相同的格子上，換成另一位有資格且當天未被排的人——不重新跑硬約束檢查，
 * 變體本來就可能帶違規，`hardViolationCount` 會如實反映。
 */
function diversifyAgainst(
  store: MockStore,
  ym: string,
  reference: Map<string, string>,
  candidate: Map<string, string>,
  minDiff: number,
  seedOffset: number,
): Map<string, string> {
  let diff = diffCellCount(reference, candidate)
  if (diff >= minDiff) return candidate
  const result = new Map(candidate)

  for (const date of datesOfYearMonth(ym)) {
    for (const areaId of areaFillOrder) {
      if (diff >= minDiff) return result
      const key = dutyKey(areaId, date)
      if (reference.get(key) !== result.get(key)) continue
      const area = store.areas.find((a) => a.id === areaId)
      if (!area) continue
      const assignedToday = new Set(
        [...result.entries()].filter(([k]) => parseDutyKey(k).date === date).map(([, sid]) => sid),
      )
      const currentStaffId = result.get(key)
      const alt = store.staff
        .filter(
          (s) =>
            s.status === 'active' &&
            s.rankCode !== 'NP' &&
            s.id !== currentStaffId &&
            s.eligibleAreaTypes.includes(area.areaTypeCode) &&
            !assignedToday.has(s.id),
        )
        .sort(
          (a, b) =>
            pseudoRandom(`${seedOffset}:${key}:${a.id}`) - pseudoRandom(`${seedOffset}:${key}:${b.id}`),
        )[0]
      if (alt) {
        result.set(key, alt.id)
        diff++
      }
    }
  }
  return result
}

export interface VariantMetrics {
  vacancies: number
  quotaFairness: number
  areaConsistency: number
  rankPreference: number
  fairnessPoint: number | null
}

export function computeVariantMetrics(store: MockStore, ym: string, dutyMap: Map<string, string>): VariantMetrics {
  const dates = datesOfYearMonth(ym)
  let vacancies = 0
  for (const date of dates) {
    for (const area of store.areas) {
      if (!dutyMap.has(dutyKey(area.id, date))) vacancies++
    }
  }

  let quotaFairness = 0
  for (const group of store.rankGroups) {
    if (group.code === 'NP') continue
    const members = store.staff.filter(
      (s) => s.status === 'active' && store.ranks.find((r) => r.code === s.rankCode)?.groupCode === group.code,
    )
    if (members.length === 0) continue
    const remainders = members.map((s) => {
      const cap = quotaCapFor(store, s.rankCode, ym)
      const points = quotaPointsForStaffInMonth(store, ym, s.id, dutyMap)
      return cap === null ? 0 : cap - points
    })
    quotaFairness += Math.max(...remainders) - Math.min(...remainders)
  }

  const dutyCountByStaff = new Map<string, number>()
  const areaCountByStaff = new Map<string, Map<string, number>>()
  for (const [key, staffId] of dutyMap) {
    dutyCountByStaff.set(staffId, (dutyCountByStaff.get(staffId) ?? 0) + 1)
    const areaCounts = areaCountByStaff.get(staffId) ?? new Map<string, number>()
    const areaId = parseDutyKey(key).areaId
    areaCounts.set(areaId, (areaCounts.get(areaId) ?? 0) + 1)
    areaCountByStaff.set(staffId, areaCounts)
  }
  let areaConsistency = 0
  for (const [staffId, counts] of areaCountByStaff) {
    const total = dutyCountByStaff.get(staffId) ?? 0
    areaConsistency += total - Math.max(...counts.values())
  }

  let rankPreference = 0
  for (const [key, staffId] of dutyMap) {
    const staff = store.staff.find((s) => s.id === staffId)
    const area = store.areas.find((a) => a.id === parseDutyKey(key).areaId)
    if (!staff || !area) continue
    if ((staff.rankCode === 'R2' || staff.rankCode === 'R3') && area.areaTypeCode !== 'ICU') rankPreference++
    if (['R4', 'R5', 'R6'].includes(staff.rankCode) && area.areaTypeCode !== 'CHIEF') rankPreference++
  }

  // 對齊 Domain 的 ScheduleScores.FairnessByGroup：各身分組內公平性點數 max − min，再加總。
  // 停用者與算不出點數的人（NP 為 null）不進比較。
  const fairnessByGroup = new Map<string, number[]>()
  for (const staff of store.staff) {
    if (staff.status !== 'active') continue
    const points = fairnessPointsForStaffInMonth(store, ym, staff.id, dutyMap)
    if (points === null) continue
    const groupCode = store.ranks.find((r) => r.code === staff.rankCode)?.groupCode
    if (!groupCode) continue
    fairnessByGroup.set(groupCode, [...(fairnessByGroup.get(groupCode) ?? []), points])
  }
  let fairnessPoint = 0
  for (const values of fairnessByGroup.values()) fairnessPoint += Math.max(...values) - Math.min(...values)

  return { vacancies, quotaFairness, areaConsistency, rankPreference, fairnessPoint }
}

/**
 * ADR-0003：最多三份具名變體，序列產生並套用多樣性約束（≥15 格不同）。
 * `variantCount` 來自 `CreateSolverJobRequest`，1～3 之間；只產生請求的份數，
 * 不永遠固定 3 份——`v-b`／`v-c` 是否存在、要不要對誰做多樣性比較都看這個數字。
 */
export function generateVariants(
  store: MockStore,
  ym: string,
  weightProfiles: Record<string, Record<string, number>>,
  labels: Record<string, { label: string; description: string }>,
  variantCount = 3,
): Variant[] {
  const ids = ['v-a', 'v-b', 'v-c'].slice(0, Math.min(Math.max(variantCount, 1), 3))
  const rawDuties = new Map<string, Map<string, string>>()
  ids.forEach((id, index) => {
    rawDuties.set(
      id,
      generateAssignment(store, ym, { seedOffset: (index + 1) * 101, weightMultipliers: weightProfiles[id] }),
    )
  })

  const dutiesA = rawDuties.get('v-a')!
  const finalDuties = new Map<string, Map<string, string>>([['v-a', dutiesA]])
  if (ids.includes('v-b')) {
    finalDuties.set('v-b', diversifyAgainst(store, ym, dutiesA, rawDuties.get('v-b')!, 15, 202))
  }
  if (ids.includes('v-c')) {
    let dutiesC = diversifyAgainst(store, ym, dutiesA, rawDuties.get('v-c')!, 15, 303)
    const dutiesB = finalDuties.get('v-b')
    if (dutiesB) dutiesC = diversifyAgainst(store, ym, dutiesB, dutiesC, 15, 304)
    finalDuties.set('v-c', dutiesC)
  }

  return ids.map((id) => {
    const dutyMap = finalDuties.get(id)!
    const metrics = computeVariantMetrics(store, ym, dutyMap)
    const violations = computeViolationsForDuties(store, ym, dutyMap)
    const hardViolationCount = violations.filter((v) => v.severity === 'hard').length
    const softScore =
      100 -
      metrics.vacancies * 5 -
      metrics.quotaFairness * 2 -
      metrics.areaConsistency * 1 -
      metrics.rankPreference * 1
    const duties: Duty[] = [...dutyMap.entries()]
      .map(([key, staffId]) => {
        const { areaId, date } = parseDutyKey(key)
        return { areaId, date, staffId, cellKey: areaCellKey(areaId, date) }
      })
      .sort((a, b) => (a.date === b.date ? a.areaId.localeCompare(b.areaId) : a.date.localeCompare(b.date)))

    return {
      id,
      label: labels[id]?.label ?? id,
      description: labels[id]?.description,
      weightProfile: weightProfiles[id] ?? {},
      metrics,
      hardViolationCount,
      softScore,
      duties,
    }
  })
}

export { previousYearMonth, nextYearMonth, previousDate, nextDate, datesOfYearMonth }

/** 已發布、但之後又改過（後端 `ScheduleHeader.EditedSincePublish` 的 mock 版，#75）。 */
export function editedSincePublish(schedule: ScheduleState): boolean {
  return schedule.status === 'published' && schedule.revision !== schedule.publishedRevision
}
