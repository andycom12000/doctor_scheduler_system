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
  DayDetail,
  Duty,
  FeasibilityReport,
  PointBoardGroup,
  Severity,
  Variant,
  VacancyByDate,
  ValidationResult,
  Violation,
} from '@/api/types'
import { areaFillOrder } from './fixtures/areas'
import {
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
  publishedAt: string | null
  /** key: `${areaId}|${date}`。未指派的格子不出現在這裡。 */
  duties: Map<string, string>
}

export function newScheduleState(yearMonth: string): ScheduleState {
  return { yearMonth, status: 'draft', revision: 0, publishedAt: null, duties: new Map() }
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

function hasPublicHolidayBetween(store: MockStore, a: string, b: string): boolean {
  let cursor = nextDate(a)
  while (cursor < b) {
    if (getCalendarDay(store, cursor).isPublicHoliday) return true
    cursor = nextDate(cursor)
  }
  return false
}

/** 公平性點數（實驗性）：查表 + 連值兩個週六加分。NP 不計，回 null。 */
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
  }

  const bonus = store.pointRules.fairness.consecutiveSaturdayBonus
  const saturdays = dutyDates.filter((d) => getCalendarDay(store, d).weekday === 6)
  for (let i = 1; i < saturdays.length; i++) {
    const gap = diffDays(saturdays[i], saturdays[i - 1])
    if (gap > 0 && gap <= bonus.windowDays && !hasPublicHolidayBetween(store, saturdays[i - 1], saturdays[i])) {
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

function makeViolation(code: string, severity: Severity, cellKeys: string[], message: string): Violation {
  return { id: violationId(code, cellKeys), code, severity, cellKeys, message }
}

function dayKindMatches(store: MockStore, date: string, kind: string): boolean {
  const day = getCalendarDay(store, date)
  if (kind === 'holiday') return day.isHoliday
  if (kind === 'publicHoliday') return day.isPublicHoliday
  return !day.isHoliday
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

  // H3 額度點數上限（NP 豁免）
  if (hardByCode.get('H3_QUOTA_CAP')?.enabled) {
    for (const staff of store.staff) {
      if (staff.rankCode === 'NP') continue
      const cap = quotaCapFor(store, staff.rankCode, ym)
      const points = quotaPointsForStaffInMonth(store, ym, staff.id, dutyMap)
      if (cap !== null && points > cap) {
        const cellKeys = [...dutyMap.entries()]
          .filter(([, sid]) => sid === staff.id)
          .map(([key]) => staffCellKey(staff.id, parseDutyKey(key).date))
        violations.push(
          makeViolation('H3_QUOTA_CAP', 'hard', cellKeys, `${staff.name} 額度點數 ${points} 超過上限 ${cap}`),
        )
      }
    }
  }

  // H4 值休休（MinGap days:3，NP 豁免，跨月讀上月尾巴）
  if (hardByCode.get('H4_MIN_GAP')?.enabled) {
    for (const staff of store.staff) {
      if (staff.rankCode === 'NP') continue
      const timeline = staffDutyTimeline(store, ym, staff.id, dutyMap)
      for (let i = 1; i < timeline.length; i++) {
        if (diffDays(timeline[i], timeline[i - 1]) < 3) {
          const cellKeys = [timeline[i - 1], timeline[i]]
            .filter((d) => d.startsWith(ym))
            .map((d) => staffCellKey(staff.id, d))
          if (cellKeys.length === 0) continue
          violations.push(
            makeViolation(
              'H4_MIN_GAP',
              'hard',
              cellKeys,
              `${staff.name} 值班間隔不足 3 天（${timeline[i - 1]} → ${timeline[i]}）`,
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

  // H6 NP 每月天數上限
  const npStaff = store.staff.find((s) => s.rankCode === 'NP')
  if (hardByCode.get('H6_NP_MONTHLY_DAYS')?.enabled && npStaff) {
    const cap = (hardByCode.get('H6_NP_MONTHLY_DAYS')?.params as { cap?: number } | undefined)?.cap ?? 20
    const npDates = [...new Set([...dutyMap.entries()].filter(([, sid]) => sid === npStaff.id).map(([key]) => parseDutyKey(key).date))]
    if (npDates.length > cap) {
      violations.push(
        makeViolation(
          'H6_NP_MONTHLY_DAYS',
          'hard',
          npDates.map((d) => staffCellKey(npStaff.id, d)),
          `${npStaff.name} 本月值班 ${npDates.length} 天，超過上限 ${cap} 天`,
        ),
      )
    }
  }

  // H7 NP 最多連六（跨月）
  if (hardByCode.get('H7_NP_MAX_CONSECUTIVE')?.enabled && npStaff) {
    const maxDays = (hardByCode.get('H7_NP_MAX_CONSECUTIVE')?.params as { days?: number } | undefined)?.days ?? 6
    const timeline = staffDutyTimeline(store, ym, npStaff.id, dutyMap)
    let runStart = 0
    for (let i = 1; i <= timeline.length; i++) {
      const broke = i === timeline.length || diffDays(timeline[i], timeline[i - 1]) !== 1
      if (broke) {
        const runLength = i - runStart
        if (runLength > maxDays) {
          const run = timeline.slice(runStart, i).filter((d) => d.startsWith(ym))
          if (run.length > 0) {
            violations.push(
              makeViolation(
                'H7_NP_MAX_CONSECUTIVE',
                'hard',
                run.map((d) => staffCellKey(npStaff.id, d)),
                `${npStaff.name} 連續值班 ${runLength} 天，超過上限 ${maxDays} 天`,
              ),
            )
          }
        }
        runStart = i
      }
    }
  }

  // S3/S4/S5/S6：Preference 逐格違規
  for (const code of ['S3_R2R3_PREFER_ICU', 'S4_R4R6_PREFER_CHIEF', 'S5_NP_LAST_RESORT', 'S6_NP_AVOID_HOLIDAY']) {
    const constraint = softByCode.get(code)
    if (!constraint || constraint.weight <= 0) continue
    const scope = constraint.scope ?? {}
    const direction = (constraint.params as { direction?: string } | undefined)?.direction

    for (const [key, staffId] of dutyMap) {
      const { areaId, date } = parseDutyKey(key)
      const staff = store.staff.find((s) => s.id === staffId)
      const area = store.areas.find((a) => a.id === areaId)
      if (!staff || !area) continue
      if (scope.rankCodes && !scope.rankCodes.includes(staff.rankCode)) continue

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

/** `組內最大剩餘額度 − 本人剩餘額度`，發布時結算，供下個月使用。NP 不參與。 */
export function computeCarryOverEntries(store: MockStore, ym: string): CarryOverEntry[] {
  const entries: CarryOverEntry[] = []
  for (const group of store.rankGroups) {
    if (group.code === 'NP') continue
    const members = store.staff.filter(
      (s) => s.status === 'active' && store.ranks.find((r) => r.code === s.rankCode)?.groupCode === group.code,
    )
    if (members.length === 0) continue
    const remainders = members.map((s) => {
      const cap = quotaCapFor(store, s.rankCode, ym)
      const points = quotaPointsForStaffInMonth(store, ym, s.id)
      return { staffId: s.id, remaining: cap === null ? 0 : cap - points }
    })
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
  const assignedElsewhereToday = new Set<string>()
  if (schedule) {
    for (const [key, sid] of schedule.duties) {
      const parsed = parseDutyKey(key)
      if (parsed.date === date && parsed.areaId !== areaId) assignedElsewhereToday.add(sid)
    }
  }

  const out: Candidate[] = []
  for (const staff of store.staff) {
    if (staff.status !== 'active') continue
    if (!staff.eligibleAreaTypes.includes(area.areaTypeCode)) continue

    const blockingReasons: string[] = []
    const warnings: string[] = []
    if (blockedToday.has(staff.id)) blockingReasons.push('該日已登記不可排班')
    if (assignedElsewhereToday.has(staff.id)) blockingReasons.push('當日已排在其他區域')

    const cap = quotaCapFor(store, staff.rankCode, ym)
    const points = quotaPointsForStaffInMonth(store, ym, staff.id)
    if (staff.rankCode !== 'NP') {
      const timeline = staffDutyTimeline(store, ym, staff.id).filter((d) => d !== date)
      if (timeline.some((d) => Math.abs(diffDays(date, d)) < 3)) blockingReasons.push('值休休間隔不足 3 天')
      if (cap !== null && points + quotaPointValueOf(store, date) > cap) blockingReasons.push('超過額度點數上限')
    } else {
      const npDates = new Set(staffDutyTimeline(store, ym, staff.id).filter((d) => d.startsWith(ym)))
      if (npDates.size >= 20) blockingReasons.push('NP 本月天數已達上限')
      warnings.push('NP 為最後人力，優先考慮一般身分')
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
      const eligible = store.staff.filter(
        (s) => s.status === 'active' && s.rankCode !== 'NP' && s.eligibleAreaTypes.includes(areaType.code),
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
      const blockedDates = blockedByStaff.get(s.id) ?? new Set<string>()
      const blockedPoints = [...blockedDates].reduce((sum, d) => sum + quotaPointValueOf(store, d), 0)
      supplyPoints += Math.max(0, cap - blockedPoints)
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
 * （資格、額度、值休休、不可排班日、NP 天數與連續上限），
 * 用權重乘數 + 決定性雜湊排序在可行候選人裡挑一個。
 * 找不到可行候選人時該格留空（H1 由驗證層抓，這裡不擋寫入）。
 *
 * 只讀上個月（`previousYearMonth(ym)`）的值班尾巴當固定輸入，
 * **不讀 `ym` 本身現有的值班表**——變體是全新假設性排班，不是在既有草稿上疊加。
 */
export function generateAssignment(store: MockStore, ym: string, options: GenerateOptions): Map<string, string> {
  const dates = datesOfYearMonth(ym)
  const result = new Map<string, string>()
  const quotaUsed = new Map<string, number>()
  const npDutyDays = new Set<string>()
  const lastDutyDate = new Map<string, string>()
  const areaCountByStaff = new Map<string, Map<string, number>>()
  const blockedByStaff = new Map<string, Set<string>>()
  for (const entry of store.blockedDays.get(ym) ?? []) {
    const set = blockedByStaff.get(entry.staffId) ?? new Set<string>()
    set.add(entry.date)
    blockedByStaff.set(entry.staffId, set)
  }
  const carryMap = new Map((store.carryOver.get(ym) ?? []).map((e) => [e.staffId, e.points]))

  const prevSchedule = store.schedules.get(previousYearMonth(ym))
  if (prevSchedule) {
    for (const [key, staffId] of prevSchedule.duties) {
      const { date } = parseDutyKey(key)
      const current = lastDutyDate.get(staffId)
      if (!current || date > current) lastDutyDate.set(staffId, date)
    }
  }

  const npId = store.staff.find((s) => s.rankCode === 'NP')?.id
  let npStreak = 0
  if (npId && prevSchedule) {
    const npDatesInPrevMonth = new Set(
      [...prevSchedule.duties.entries()].filter(([, sid]) => sid === npId).map(([key]) => parseDutyKey(key).date),
    )
    for (const d of [...datesOfYearMonth(previousYearMonth(ym))].reverse()) {
      if (npDatesInPrevMonth.has(d)) npStreak++
      else break
    }
  }

  const weight = (code: string) => options.weightMultipliers?.[code] ?? 1

  for (const date of dates) {
    const day = getCalendarDay(store, date)
    const assignedToday = new Set<string>()
    let npWorkedToday = false

    for (const areaId of areaFillOrder) {
      const area = store.areas.find((a) => a.id === areaId)
      if (!area) continue

      const feasible = store.staff.filter((s) => {
        if (s.status !== 'active') return false
        if (!s.eligibleAreaTypes.includes(area.areaTypeCode)) return false
        if (assignedToday.has(s.id)) return false
        if (blockedByStaff.get(s.id)?.has(date)) return false

        if (s.rankCode === 'NP') {
          if (npDutyDays.size >= 20) return false
          if (npStreak + 1 > 6) return false
          return true
        }
        const last = lastDutyDate.get(s.id)
        if (last && diffDays(date, last) < 3) return false
        const cap = quotaCapFor(store, s.rankCode, ym)
        const used = quotaUsed.get(s.id) ?? 0
        if (cap !== null && used + day.quotaPointValue > cap) return false
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
        score += remaining * 2 * (weight('S1_QUOTA_FAIRNESS') / 100)
        score -= (carryMap.get(s.id) ?? 0) * 0.5

        const thisAreaCount = areaCountByStaff.get(s.id)?.get(areaId) ?? 0
        score += thisAreaCount * 3 * (weight('S2_AREA_CONSISTENCY') / 40)

        if ((s.rankCode === 'R2' || s.rankCode === 'R3') && area.areaTypeCode === 'ICU') {
          score += 5 * (weight('S3_R2R3_PREFER_ICU') / 50)
        }
        if (['R4', 'R5', 'R6'].includes(s.rankCode) && area.areaTypeCode === 'CHIEF') {
          score += 5 * (weight('S4_R4R6_PREFER_CHIEF') / 50)
        }
        if (s.rankCode === 'NP') {
          score -= 8 * (weight('S5_NP_LAST_RESORT') / 60)
          if (day.isHoliday) score -= 6 * (weight('S6_NP_AVOID_HOLIDAY') / 30)
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
      if (best.rankCode === 'NP') {
        npDutyDays.add(date)
        npWorkedToday = true
      }
    }

    npStreak = npWorkedToday ? npStreak + 1 : 0
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

  let fairnessPoint = 0
  for (const staff of store.staff) {
    if (staff.rankCode === 'NP') continue
    fairnessPoint += fairnessPointsForStaffInMonth(store, ym, staff.id, dutyMap) ?? 0
  }

  return { vacancies, quotaFairness, areaConsistency, rankPreference, fairnessPoint }
}

/** ADR-0003：三份具名變體，序列產生並套用多樣性約束（≥15 格不同）。 */
export function generateVariants(
  store: MockStore,
  ym: string,
  weightProfiles: Record<string, Record<string, number>>,
  labels: Record<string, { label: string; description: string }>,
): Variant[] {
  const ids = ['v-a', 'v-b', 'v-c']
  const rawDuties = new Map<string, Map<string, string>>()
  ids.forEach((id, index) => {
    rawDuties.set(
      id,
      generateAssignment(store, ym, { seedOffset: (index + 1) * 101, weightMultipliers: weightProfiles[id] }),
    )
  })

  const dutiesA = rawDuties.get('v-a')!
  let dutiesB = rawDuties.get('v-b')!
  let dutiesC = rawDuties.get('v-c')!
  dutiesB = diversifyAgainst(store, ym, dutiesA, dutiesB, 15, 202)
  dutiesC = diversifyAgainst(store, ym, dutiesA, dutiesC, 15, 303)
  dutiesC = diversifyAgainst(store, ym, dutiesB, dutiesC, 15, 304)
  const finalDuties = new Map([
    ['v-a', dutiesA],
    ['v-b', dutiesB],
    ['v-c', dutiesC],
  ])

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
