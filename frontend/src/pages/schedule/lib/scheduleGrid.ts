/**
 * 三個檢視分頁共用的純函式：cellKey 索引、值班表攤平、人員名冊（由點數看板推導，
 * 這張畫面的允許端點清單裡沒有 `/staff`，`PointBoardRow` 已經帶 `name`／`rankCode`，
 * 見 PR 說明）。
 *
 * cellKey 慣例照 CONTEXT.md：逐格規則 `area:{areaId}:{date}`，個人序列
 * `staff:{staffId}:{date}`——這裡自己組字串，不信任 API 回傳的 `Duty.cellKey`
 * （契約上那個欄位非必要，且 cellKey 定義上是「渲染層的索引鍵」，前端自己算才不會
 * 因為某個端點忘了帶而漏掉）。
 */
import type { CalendarDay, Duty, PointBoardGroup, Staff } from '@/api/types'

export function areaCellKey(areaId: string, date: string): string {
  return `area:${areaId}:${date}`
}

export function staffCellKey(staffId: string, date: string): string {
  return `staff:${staffId}:${date}`
}

export interface StaffDirectoryEntry {
  staffId: string
  name: string
  rankCode: string
  groupCode: string
  groupName: string
  /** 組在點數看板裡出現的順序（0-based），畫面用來配 `--group-1..4`；查無所屬組時是 `null`。 */
  groupIndex: number | null
  status: 'active' | 'inactive'
}

/**
 * 由點數看板攤平出人員名冊；點數看板本來就只列在職，選配的 `staffList`（`GET /staff`）
 * 補在職名冊查不到的人——最常見的是「當月仍有班、後來被停用」的人，區域 × 日／日 × 人
 * 格內至少要印得出名字與身分，不能因為不在點數看板裡就整格空白或只剩 staffId。
 */
export function buildStaffDirectory(
  groups: readonly PointBoardGroup[],
  staffList: readonly Staff[] = [],
): Map<string, StaffDirectoryEntry> {
  const directory = new Map<string, StaffDirectoryEntry>()
  groups.forEach((group, groupIndex) => {
    for (const row of group.rows) {
      directory.set(row.staffId, {
        staffId: row.staffId,
        name: row.name,
        rankCode: row.rankCode,
        groupCode: group.groupCode,
        groupName: group.groupName,
        groupIndex,
        status: 'active',
      })
    }
  })
  for (const staff of staffList) {
    if (directory.has(staff.id)) continue
    directory.set(staff.id, {
      staffId: staff.id,
      name: staff.name,
      rankCode: staff.rankCode,
      groupCode: '',
      groupName: '',
      groupIndex: null,
      status: staff.status,
    })
  }
  return directory
}

/** 姓名簡稱（格內顯示用）：取前兩個字，非中文姓名也至少截兩碼。 */
export function abbreviate(name: string): string {
  return [...name].slice(0, 2).join('')
}

/** 兩邊名冊都查不到時的最後備援（理論上不該發生——人員只會停用不會消失）：取 `staffId` 最後一段。 */
export function shortStaffCode(staffId: string): string {
  const dash = staffId.lastIndexOf('-')
  return dash >= 0 ? staffId.slice(dash + 1) : staffId
}

/** `area:{areaId}|{date}` → `staffId`，區域 × 日檢視查表用。 */
export function dutiesByArea(duties: readonly Duty[]): Map<string, string> {
  const map = new Map<string, string>()
  for (const duty of duties) map.set(`${duty.areaId}|${duty.date}`, duty.staffId)
  return map
}

/** `staffId|date` → `areaId`，日 × 人檢視查表用。 */
export function dutiesByStaff(duties: readonly Duty[]): Map<string, string> {
  const map = new Map<string, string>()
  for (const duty of duties) map.set(`${duty.staffId}|${duty.date}`, duty.areaId)
  return map
}

const WEEKDAY_LABELS = ['日', '一', '二', '三', '四', '五', '六']

export function weekdayLabel(weekday: number): string {
  return WEEKDAY_LABELS[weekday] ?? ''
}

export function dayOfMonth(date: string): string {
  return date.slice(-2)
}

/** 表頭需要的行事曆摘要，避免元件各自重算。 */
export interface DayColumn {
  date: string
  dd: string
  weekday: string
  isHoliday: boolean
  quotaPointValue: number
}

export function toDayColumns(days: readonly CalendarDay[]): DayColumn[] {
  return days.map((day) => ({
    date: day.date,
    dd: dayOfMonth(day.date),
    weekday: weekdayLabel(day.weekday),
    isHoliday: day.isHoliday,
    quotaPointValue: day.quotaPointValue,
  }))
}

/** 每日已填補區域數（區域數 − 當日空缺數），日 × 人與區域 × 日的「填補」欄共用。 */
export function filledCountByDate(
  totalAreas: number,
  vacancyCountByDate: ReadonlyMap<string, number>,
  date: string,
): number {
  return totalAreas - (vacancyCountByDate.get(date) ?? 0)
}

export function vacancyCountMap(byDate: readonly { date: string; count: number }[]): Map<string, number> {
  return new Map(byDate.map((entry) => [entry.date, entry.count]))
}
