/**
 * 違規側欄「點一筆捲到該格」：從 `cellKeys[0]` 解析出該去哪個分頁、哪個 DOM 節點。
 * 純函式，元件只需要 `document.getElementById(domIdForCellKey(key))?.scrollIntoView(...)`。
 */
export type ScheduleTab = 'area-by-day' | 'day-by-staff'

export interface ParsedCellKey {
  kind: 'area' | 'staff'
  id: string
  date: string
}

/** cellKey 形如 `area:{areaId}:{date}` 或 `staff:{staffId}:{date}`；`areaId`／`staffId` 本身不含冒號。 */
export function parseCellKey(cellKey: string): ParsedCellKey | null {
  const [kind, id, date] = cellKey.split(':')
  if ((kind !== 'area' && kind !== 'staff') || !id || !date) return null
  return { kind, id, date }
}

export function tabForCellKey(cellKey: string): ScheduleTab | null {
  const parsed = parseCellKey(cellKey)
  if (!parsed) return null
  return parsed.kind === 'area' ? 'area-by-day' : 'day-by-staff'
}

/** 與各檢視格子元素上實際掛的 `id` 屬性一致（見 AreaByDayGrid.vue／DayByStaffGrid.vue）。 */
export function domIdForCellKey(cellKey: string): string | null {
  const parsed = parseCellKey(cellKey)
  if (!parsed) return null
  return `sc-cell-${parsed.kind}-${parsed.id}-${parsed.date}`
}
