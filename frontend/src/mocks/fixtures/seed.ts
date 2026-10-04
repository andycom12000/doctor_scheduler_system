/**
 * 初始狀態：一份「2026-08 已發布」的值班表（乾淨、供跨月尾巴與月結轉示範），
 * 加一份「2026-09 草稿」（用同一套貪婪演算法填出來，故意留 2 個空格
 * 與 2 個硬違規，讓 UI 有東西可看）。
 *
 * 只在 `store.ts` 建構初始狀態時呼叫一次；`resetStore()` 會重新執行整個模組。
 */
import type { BlockedDayEntry } from '@/api/types'
import {
  computeCarryOverEntries,
  datesOfYearMonth,
  dutyKey,
  ensureSchedule,
  generateAssignment,
  nextYearMonth,
  parseDutyKey,
  quotaCapFor,
  quotaPointsForStaffInMonth,
} from '../domain'
import type { MockStore } from '../store'

const AUGUST = '2026-08'
const SEPTEMBER = '2026-09'

const septemberBlockedDays: BlockedDayEntry[] = [
  { staffId: 'staff-003', date: '2026-09-05' },
  { staffId: 'staff-003', date: '2026-09-06' },
  { staffId: 'staff-007', date: '2026-09-12' },
  { staffId: 'staff-011', date: '2026-09-01' },
  { staffId: 'staff-014', date: '2026-09-19' },
  { staffId: 'staff-018', date: '2026-09-08' },
  { staffId: 'staff-018', date: '2026-09-09' },
  { staffId: 'staff-022', date: '2026-09-26' },
  { staffId: 'staff-027', date: '2026-09-13' },
  { staffId: 'staff-031', date: '2026-09-30' },
]

/**
 * 保護網：seed 資料是手工注入違規，任何一步失手都可能造出「同一人同一天
 * 被排進兩個區」這種畸形狀態——這不是任何一條規則的違規（H1–H7 都是逐人或
 * 逐格檢查，沒有一條在管「同一天只能值一區」），沒有規則抓得到，UI 會顯示出
 * 兩格都「有人」但其實是同一人重複出現的詭異畫面。建構期間直接炸掉比留給
 * 前端排查更快。
 */
function assertNoStaffDoubleBookedSameDay(duties: Map<string, string>, label: string): void {
  const seenAreaByStaffDate = new Map<string, string>()
  for (const [key, staffId] of duties) {
    const { areaId, date } = parseDutyKey(key)
    const dayKey = `${staffId}|${date}`
    const existingAreaId = seenAreaByStaffDate.get(dayKey)
    if (existingAreaId && existingAreaId !== areaId) {
      throw new Error(
        `[seed:${label}] ${staffId} 在 ${date} 同時被排進 ${existingAreaId} 與 ${areaId}，seed 資料有誤`,
      )
    }
    seenAreaByStaffDate.set(dayKey, areaId)
  }
}

export function buildSeedSchedules(store: MockStore): void {
  // --- 2026-08：已發布，乾淨（作為 2026-09 的跨月尾巴與月結轉來源） ---
  const augustDuties = generateAssignment(store, AUGUST, { seedOffset: 1 })
  assertNoStaffDoubleBookedSameDay(augustDuties, AUGUST)
  const august = ensureSchedule(store, AUGUST)
  august.duties = augustDuties
  august.status = 'published'
  august.revision = 1
  august.publishedVersion = 1
  august.publishedRevision = 1
  august.publishedAt = '2026-08-31T10:00:00.000Z'

  store.carryOver.set(nextYearMonth(AUGUST), computeCarryOverEntries(store, AUGUST))

  // --- 2026-09：草稿，先登記不可排班日再排 ---
  store.blockedDays.set(SEPTEMBER, septemberBlockedDays)

  const septemberDuties = generateAssignment(store, SEPTEMBER, { seedOffset: 2 })

  // demo 用途：故意留 2 個空格，讓「空缺」與變體密度圖有東西可比。
  septemberDuties.delete(dutyKey('area-c', '2026-09-10'))
  septemberDuties.delete(dutyKey('area-b', '2026-09-22'))

  // demo 用途：故意製造 1 個 H4_MIN_GAP 違規——挑一位「非 NP、9/14 有值班」的人，
  // 9/15 也排他（不同區）。挑非 NP 是因為 H4 豁免 NP（H4_MIN_GAP.scope.exemptRankCodes）；
  // 只在他 9/15 原本沒班時才動手，避免造出同人同日兩區的畸形狀態。
  let gapOffenderId: string | null = null
  for (const [key, staffId] of septemberDuties) {
    if (parseDutyKey(key).date !== '2026-09-14') continue
    const staff = store.staff.find((s) => s.id === staffId)
    if (staff && staff.rankCode !== 'NP') {
      gapOffenderId = staffId
      break
    }
  }
  if (gapOffenderId) {
    const alreadyWorks15 = [...septemberDuties.entries()].some(
      ([key, staffId]) => staffId === gapOffenderId && parseDutyKey(key).date === '2026-09-15',
    )
    if (!alreadyWorks15) {
      septemberDuties.set(dutyKey('area-b', '2026-09-15'), gapOffenderId)
    }
  }

  // demo 用途：故意製造 1 個 H3_QUOTA_CAP 違規——找一般病房裡剩餘額度最少的人，
  // 在他這個月原本沒班的日子裡多塞一格，確保不會造出同人同日兩區的畸形狀態。
  let overCapStaffId: string | null = null
  let minRemaining = Number.POSITIVE_INFINITY
  for (const staff of store.staff) {
    if (staff.rankCode === 'NP') continue
    if (!store.eligibilityMatrix.matrix[staff.rankCode]?.WARD) continue
    const cap = quotaCapFor(store, staff.rankCode, SEPTEMBER)
    if (cap === null) continue
    const points = quotaPointsForStaffInMonth(store, SEPTEMBER, staff.id, septemberDuties)
    const remaining = cap - points
    if (remaining < minRemaining) {
      minRemaining = remaining
      overCapStaffId = staff.id
    }
  }
  if (overCapStaffId) {
    const workedDates = new Set(
      [...septemberDuties.entries()]
        .filter(([, staffId]) => staffId === overCapStaffId)
        .map(([key]) => parseDutyKey(key).date),
    )
    const freeDates = datesOfYearMonth(SEPTEMBER).filter((date) => !workedDates.has(date))
    const targetDate = freeDates.find((date) => date > '2026-09-15') ?? freeDates[0]
    if (targetDate) {
      septemberDuties.set(dutyKey('area-c', targetDate), overCapStaffId)
    }
  }

  assertNoStaffDoubleBookedSameDay(septemberDuties, SEPTEMBER)

  const september = ensureSchedule(store, SEPTEMBER)
  september.duties = septemberDuties
  september.status = 'draft'
  september.revision = 1
  september.publishedAt = null
}
