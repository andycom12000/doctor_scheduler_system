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
  dutyKey,
  ensureSchedule,
  generateAssignment,
  nextYearMonth,
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

export function buildSeedSchedules(store: MockStore): void {
  // --- 2026-08：已發布，乾淨（作為 2026-09 的跨月尾巴與月結轉來源） ---
  const augustDuties = generateAssignment(store, AUGUST, { seedOffset: 1 })
  const august = ensureSchedule(store, AUGUST)
  august.duties = augustDuties
  august.status = 'published'
  august.revision = 1
  august.publishedAt = '2026-08-31T10:00:00.000Z'

  store.carryOver.set(nextYearMonth(AUGUST), computeCarryOverEntries(store, AUGUST))

  // --- 2026-09：草稿，先登記不可排班日再排 ---
  store.blockedDays.set(SEPTEMBER, septemberBlockedDays)

  const septemberDuties = generateAssignment(store, SEPTEMBER, { seedOffset: 2 })

  // demo 用途：故意留 2 個空格，讓「空缺」與變體密度圖有東西可比。
  septemberDuties.delete(dutyKey('area-c', '2026-09-10'))
  septemberDuties.delete(dutyKey('area-b', '2026-09-22'))

  // demo 用途：故意製造 1 個 H4_MIN_GAP 違規——同一人被排在相鄰兩天。
  const gapOffender = septemberDuties.get(dutyKey('area-a', '2026-09-14'))
  if (gapOffender) {
    septemberDuties.set(dutyKey('area-b', '2026-09-15'), gapOffender)
  }

  // demo 用途：故意製造 1 個 H3_QUOTA_CAP 違規——找一般病房裡剩餘額度最少的人再多塞一格。
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
    septemberDuties.set(dutyKey('area-c', '2026-09-20'), overCapStaffId)
  }

  const september = ensureSchedule(store, SEPTEMBER)
  september.duties = septemberDuties
  september.status = 'draft'
  september.revision = 1
  september.publishedAt = null
}
