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

export interface JumpTarget {
  tab: ScheduleTab
  domId: string | null
}

/**
 * 違規側欄「點一筆」該去哪裡：右側欄（利用率＋違規）限定只在「區域 × 日」顯示（#53）之後，
 * `staff:` 開頭的違規（H3／H4／H6／H7）不能再單純切去「日 × 人」——那個分頁沒有側欄，
 * 點了違規清單本身就消失。改成優先反查「那天在哪個區值班」，留在區域 × 日；
 * 查不到值班紀錄（理論上不該發生，保留為防禦）才退回切去日 × 人。
 */
export function resolveJumpTarget(cellKey: string, dutyMapByStaff: ReadonlyMap<string, readonly string[]>): JumpTarget | null {
  const parsed = parseCellKey(cellKey)
  if (!parsed) return null
  if (parsed.kind === 'area') {
    return { tab: 'area-by-day', domId: domIdForCellKey(cellKey) }
  }
  // 同人同日兩區（X1）時有多個，挑 areaId 排序後的第一個，確定性
  const areaId = dutyMapByStaff.get(`${parsed.id}|${parsed.date}`)?.[0]
  if (areaId) {
    return { tab: 'area-by-day', domId: domIdForCellKey(`area:${areaId}:${parsed.date}`) }
  }
  return { tab: 'day-by-staff', domId: domIdForCellKey(cellKey) }
}
