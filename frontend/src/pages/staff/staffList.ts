/**
 * SCREEN 06 人員維護的純函式：篩選、依身分組排序、名稱查表、錯誤碼判讀。
 * 全部不碰 DOM，方便進 vitest（`vitest.config.ts` 的 `environment: 'node'`）。
 */
import { ApiError } from '@/api/client'
import type {
  AreaType,
  PointBoardGroup,
  PointBoardRow,
  Rank,
  RankGroup,
  Staff,
  StaffCounts,
  StaffStatus,
} from '@/api/types'

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

    return compareName(a.name, b.name)
  })
}

/** 姓名排序走繁中語系（注音序），不用 UTF-16 碼位——後者對中文是無意義的順序。 */
export function compareName(a: string, b: string): number {
  return a.localeCompare(b, 'zh-Hant')
}

/** 右側表單的可編輯欄位。 */
export interface StaffDraft {
  employeeNo: string
  name: string
  rankCode: string
}

/**
 * 表單與開啟當下的基準值不同就算有未儲存的變更。姓名與員編比對時去掉前後空白
 * （儲存時本來就會 trim，只差空白不算改過）；`baseline` 為 `null`（沒開表單）一律不算。
 */
export function isDraftDirty(draft: StaffDraft, baseline: StaffDraft | null): boolean {
  if (!baseline) return false
  return (
    draft.employeeNo.trim() !== baseline.employeeNo.trim() ||
    draft.name.trim() !== baseline.name.trim() ||
    draft.rankCode !== baseline.rankCode
  )
}

/**
 * 身分設定載入失敗（或還是空的）時，新增人員無從選身分、儲存鈕會恆灰；回一句說明給表單顯示，
 * 讓使用者知道是這個原因而不是自己漏填。載入中、或身分清單有東西時回 `null`。
 */
export function ranksUnavailableMessage(
  state: { loading: boolean; error: unknown; rankCount: number },
  describe: (err: unknown) => string,
): string | null {
  if (state.error) return `身分清單載入失敗，暫時無法儲存：${describe(state.error)}`
  if (!state.loading && state.rankCount === 0) return '身分清單是空的，暫時無法儲存。'
  return null
}

/** 篩選 + 排序一次做完，`index.vue` 的清單只需要呼叫這個。 */
export function visibleStaff(staff: Staff[], ranks: Rank[], groups: RankGroup[], options: StaffFilterOptions): Staff[] {
  return sortByRankGroup(filterStaff(staff, ranks, options), ranks, groups)
}

/** 篩選列右側的「共 X 人 · 在職 Y · 停用 Z · 無分頁」摘要，`X` 是 `active + inactive`。 */
export function staffCountsLabel(counts: StaffCounts): string {
  const active = counts.active ?? 0
  const inactive = counts.inactive ?? 0
  return `共 ${active + inactive} 人 · 在職 ${active} · 停用 ${inactive} · 無分頁`
}

/**
 * 點數看板依身分組分區，這裡攤平找某人那一列；點數看板只列在職者
 * （`ScheduleQueries` 組看板時就先濾掉停用人員），停用中的人員或月中才加入、
 * 尚未出現在任何一組的人員查不到列回 `undefined`，呼叫端決定要顯示「—」還是別的訊息。
 */
export function findPointBoardRow(groups: PointBoardGroup[], staffId: string): PointBoardRow | undefined {
  for (const group of groups) {
    const row = group.rows.find((r) => r.staffId === staffId)
    if (row) return row
  }
  return undefined
}

/**
 * 右側表單姓名旁的用量摘要，`ym` 一律印進字串——`/staff` 沒有 `:ym` 路由參數，
 * 這裡的 `ym` 是 `useYearMonth` 模組層最後一次看到的年月，不寫清楚會被誤讀成「本月」。
 * NP 的 `quotaCap` 為 `null`（額度點數不計），比照 `PointBoardPanel.vue` 的慣例改印值班天數
 * （`duties`），不印 `quotaPoints`（NP 本來就不算額度點數，印出來沒有意義）；一般身分的點數
 * 明確標「額度點數」（CONTEXT.md：講點數不指明是哪一套一律視為錯誤）。
 * 查不到列回 `null`，呼叫端決定顯示「—」還是「{ym} 尚無值班表」。
 */
export function describeQuotaLoad(row: PointBoardRow | undefined, ym: string): string | null {
  if (!row) return null
  if (row.quotaCap === null) return `${ym} ${row.duties} 天 · 額度點數不計 · 假日 ${row.holidayDuties} 班`
  return `${ym} 額度點數 ${row.quotaPoints}/${row.quotaCap} · 餘 ${row.quotaRemaining} · 假日 ${row.holidayDuties} 班`
}

/** 點數看板 404（該月尚無值班表）判讀，用 `status` 判斷、不字串比對訊息內容。 */
export function isNotFoundError(err: unknown): boolean {
  return err instanceof ApiError && err.status === 404
}

export interface AreaTypeChip {
  code: string
  name: string
  eligible: boolean
}

/**
 * 可值區域類型三個晶片全部顯示（可值的亮、不可值的灰），順序依 `areaTypes` 給定的順序，
 * 不因為某人不可值某類型就把它從清單裡拿掉。
 */
export function areaTypeChips(areaTypes: AreaType[], eligible: string[]): AreaTypeChip[] {
  return areaTypes.map((type) => ({ code: type.code, name: type.name, eligible: eligible.includes(type.code) }))
}
