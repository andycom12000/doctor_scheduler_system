/**
 * SCREEN 06 人員維護的純函式：篩選、依身分組排序、名稱查表、錯誤碼判讀。
 * 全部不碰 DOM，方便進 vitest（`vitest.config.ts` 的 `environment: 'node'`）。
 */
import { ApiError } from '@/api/client'
import type { AreaType, ErrorCode, Rank, RankGroup, Staff, StaffStatus } from '@/api/types'

export type StatusFilter = StaffStatus | 'all'
export type GroupFilter = string | 'all'

export interface StaffFilterOptions {
  /** 姓名或員編關鍵字，前後空白會被去除、不分大小寫。 */
  search: string
  group: GroupFilter
  status: StatusFilter
}

/** `rankCode` → `groupCode`，查不到回 `null`（例如身分被刪掉但人員紀錄還沒更新）。 */
export function groupCodeOfRank(ranks: Rank[], rankCode: string): string | null {
  return ranks.find((r) => r.code === rankCode)?.groupCode ?? null
}

/** `rankCode` → 顯示用身分名稱，查不到時退回代碼本身。 */
export function rankNameOf(ranks: Rank[], rankCode: string): string {
  return ranks.find((r) => r.code === rankCode)?.name ?? rankCode
}

/** `groupCode` → 顯示用身分組名稱，查不到時退回代碼本身；`null` 回 `—`。 */
export function groupNameOf(groups: RankGroup[], groupCode: string | null): string {
  if (!groupCode) return '—'
  return groups.find((g) => g.code === groupCode)?.name ?? groupCode
}

/** 可值區域類型代碼陣列 → 顯示用名稱，以「、」連接；查不到名稱時退回代碼，空陣列回 `—`。 */
export function areaTypeNames(codes: string[], areaTypes: AreaType[]): string {
  if (codes.length === 0) return '—'
  return codes.map((code) => areaTypes.find((t) => t.code === code)?.name ?? code).join('、')
}

/** 搜尋（姓名／員編）＋ 身分組 ＋ 狀態，三個篩選都在前端做（`GET /staff` 一次全帶）。 */
export function filterStaff(staff: Staff[], ranks: Rank[], options: StaffFilterOptions): Staff[] {
  const keyword = options.search.trim().toLowerCase()
  return staff.filter((s) => {
    if (options.status !== 'all' && s.status !== options.status) return false
    if (options.group !== 'all' && groupCodeOfRank(ranks, s.rankCode) !== options.group) return false
    if (keyword) {
      const inName = s.name.toLowerCase().includes(keyword)
      const inEmployeeNo = s.employeeNo.toLowerCase().includes(keyword)
      if (!inName && !inEmployeeNo) return false
    }
    return true
  })
}

/**
 * 依身分組排序（依 `RankSettings.groups` 的既定順序），組內再依 `RankSettings.ranks`
 * 的既定順序，最後依姓名排序。查不到組別／身分順序的排最後，不讓找不到的資料插隊。
 */
export function sortByRankGroup(staff: Staff[], ranks: Rank[], groups: RankGroup[]): Staff[] {
  const groupOrder = new Map(groups.map((g, index) => [g.code, index]))
  const rankOrder = new Map(ranks.map((r, index) => [r.code, index]))
  const orderOf = (map: Map<string, number>, key: string | null) =>
    (key !== null ? map.get(key) : undefined) ?? Number.MAX_SAFE_INTEGER

  return [...staff].sort((a, b) => {
    const groupDiff =
      orderOf(groupOrder, groupCodeOfRank(ranks, a.rankCode)) - orderOf(groupOrder, groupCodeOfRank(ranks, b.rankCode))
    if (groupDiff !== 0) return groupDiff

    const rankDiff = orderOf(rankOrder, a.rankCode) - orderOf(rankOrder, b.rankCode)
    if (rankDiff !== 0) return rankDiff

    return a.name < b.name ? -1 : a.name > b.name ? 1 : 0
  })
}

/** 篩選 + 排序一次做完，`index.vue` 的清單只需要呼叫這個。 */
export function visibleStaff(staff: Staff[], ranks: Rank[], groups: RankGroup[], options: StaffFilterOptions): Staff[] {
  return sortByRankGroup(filterStaff(staff, ranks, options), ranks, groups)
}

/**
 * `ApiError` → 契約 `ErrorCode`。`src/api/errors.ts` 有一份一模一樣的邏輯但沒有 export，
 * 這裡自己留一份（不能改 `src/api/`），只給 `EMPLOYEE_NO_TAKEN`／`STAFF_HAS_DUTIES` 兩個
 * 分支判斷用，一般訊息仍交給 `describeError`。
 */
export function errorCodeOf(err: unknown): ErrorCode | null {
  if (!(err instanceof ApiError)) return null
  const body = err.body
  if (
    body &&
    typeof body === 'object' &&
    'error' in body &&
    body.error &&
    typeof body.error === 'object' &&
    'code' in body.error &&
    typeof body.error.code === 'string'
  ) {
    return body.error.code as ErrorCode
  }
  return null
}
