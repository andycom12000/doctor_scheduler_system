/**
 * SCREEN 01 V02 月份空狀態（issue #62）的純函式：欄序（人員依身分組排列）、
 * 身分組色帶分段、身分組交界欄，以及訊息面板貼齊格線的幾何換算。
 *
 * 欄序與分組**一律由 `/api/staff` 與 `/settings/ranks` 動態算**，不寫死索引或人數——
 * 名冊變動（休假、離職、7 月輪替）時色帶寬度與交界欄線要跟著變。
 */
import type { Rank, RankGroup, Staff } from '@/api/types'

export interface EmptyGridColumn {
  staffId: string
  name: string
  rankCode: string
  /** 查不到對應身分組時是 `null`；正常名冊資料下不會發生。 */
  groupCode: string | null
  /** 在 `groups` 陣列裡的順序（0-based），畫面用來配色帶第 N 階與 `G{N+1}` 標籤。 */
  groupIndex: number | null
}

/**
 * 依身分組排列人員欄：組間依 `groups` 給定的順序，組內依 `ranks` 給定的順序，
 * 最後依姓名排序。查不到身分組的人員一律歸到最後一組，不讓查無資料的人打散版面
 * （色帶寬度是 `Σ組內人數`，缺一個歸屬會讓色帶總寬跟欄數對不起來）。
 */
export function orderStaffByGroup(
  staff: readonly Staff[],
  ranks: readonly Rank[],
  groups: readonly RankGroup[],
): EmptyGridColumn[] {
  const rankByCode = new Map(ranks.map((r) => [r.code, r]))
  const groupOrder = new Map(groups.map((g, index) => [g.code, index]))
  const rankOrder = new Map(ranks.map((r, index) => [r.code, index]))
  const fallbackGroupIndex = groups.length > 0 ? groups.length - 1 : null

  const columns: EmptyGridColumn[] = staff.map((s) => {
    const rank = rankByCode.get(s.rankCode)
    const groupCode = rank?.groupCode ?? null
    const groupIndex = groupCode !== null ? groupOrder.get(groupCode) ?? fallbackGroupIndex : fallbackGroupIndex
    return { staffId: s.id, name: s.name, rankCode: s.rankCode, groupCode, groupIndex }
  })

  return [...columns].sort((a, b) => {
    const ga = a.groupIndex ?? Number.MAX_SAFE_INTEGER
    const gb = b.groupIndex ?? Number.MAX_SAFE_INTEGER
    if (ga !== gb) return ga - gb
    const ra = rankOrder.get(a.rankCode) ?? Number.MAX_SAFE_INTEGER
    const rb = rankOrder.get(b.rankCode) ?? Number.MAX_SAFE_INTEGER
    if (ra !== rb) return ra - rb
    return a.name < b.name ? -1 : a.name > b.name ? 1 : 0
  })
}

export interface EmptyGridBand {
  /** 通用位置標籤（`G1`…`G4`），不是 `RankGroup.code`——設計稿的色帶標籤是位置序，不是代碼本身。 */
  label: string
  count: number
  groupIndex: number
}

/** 色帶分段：只列有人的組別，寬度＝組內人數，順序照 `groups`（0-based → G1…）。 */
export function groupBandsOf(columns: readonly EmptyGridColumn[]): EmptyGridBand[] {
  const counts = new Map<number, number>()
  for (const col of columns) {
    if (col.groupIndex === null) continue
    counts.set(col.groupIndex, (counts.get(col.groupIndex) ?? 0) + 1)
  }
  return [...counts.entries()]
    .sort(([a], [b]) => a - b)
    .map(([groupIndex, count]) => ({ label: `G${groupIndex + 1}`, count, groupIndex }))
}

/**
 * 身分組交界欄（0-based 索引）：每個身分組的第一欄，含最左邊那一組（第 0 欄）。
 * 由 `columns` 動態算，不寫死索引——名冊或分組改變時交界位置要跟著移動。
 */
export function groupBoundaryColumns(columns: readonly EmptyGridColumn[]): Set<number> {
  const boundaries = new Set<number>()
  columns.forEach((col, i) => {
    if (i === 0 || col.groupIndex !== columns[i - 1].groupIndex) boundaries.add(i)
  })
  return boundaries
}

export interface PanelGeometry {
  colStart: number
  colSpan: number
  rowStart: number
  rowSpan: number
}

const PANEL_COL_FRACTION = 0.36
const PANEL_ROW_FRACTION = 0.34

/**
 * 面板格數換算（規格 §5.2）：`colSpan = round(nCols * 0.36)`、
 * `colStart = round(nCols * (1 - 0.36) / 2)`，列向同理（0.34）。
 * 33 欄 → colSpan 12、colStart 11；30 列 → rowSpan 10、rowStart 10。
 */
export function computePanelGeometry(nCols: number, nRows: number): PanelGeometry {
  return {
    colSpan: Math.round(nCols * PANEL_COL_FRACTION),
    colStart: Math.round((nCols * (1 - PANEL_COL_FRACTION)) / 2),
    rowSpan: Math.round(nRows * PANEL_ROW_FRACTION),
    rowStart: Math.round((nRows * (1 - PANEL_ROW_FRACTION)) / 2),
  }
}

/** 左側日期欄寬（規格 §4 固定尺寸）。 */
export const LABEL_WIDTH_PX = 44
/** 表頭總高：身分組色帶 18 + 姓名欄頭 76 + 身分列 24（規格 §4 固定尺寸）。 */
export const HEADER_HEIGHT_PX = 118

export interface PanelRect {
  left: string
  top: string
  width: string
  height: string
}

/**
 * 面板 CSS 位置：貼齊格線交點、置中，換算全部用格數＋固定尺寸，不用整月固定 px
 * （規格 §5.2 的公式）。`nCols`／`nRows` 為 0 時（資料還沒載完）回傳零尺寸，不算出 NaN。
 */
export function panelRectStyle(nCols: number, nRows: number): PanelRect {
  if (nCols <= 0 || nRows <= 0) {
    return { left: '0px', top: '0px', width: '0px', height: '0px' }
  }
  const { colStart, colSpan, rowStart, rowSpan } = computePanelGeometry(nCols, nRows)
  return {
    left: `calc(${LABEL_WIDTH_PX}px + (100% - ${LABEL_WIDTH_PX}px) * ${colStart} / ${nCols})`,
    top: `calc(${HEADER_HEIGHT_PX}px + (100% - ${HEADER_HEIGHT_PX}px) * ${rowStart} / ${nRows})`,
    width: `calc((100% - ${LABEL_WIDTH_PX}px) * ${colSpan} / ${nCols})`,
    height: `calc((100% - ${HEADER_HEIGHT_PX}px) * ${rowSpan} / ${nRows})`,
  }
}

/** 面板標題「{YYYY} 年 {M} 月尚未產生班表」，`ym` 是 `YYYY-MM`。 */
export function emptyStateTitle(ym: string): string {
  const [year, month] = ym.split('-')
  return `${year} 年 ${Number(month)} 月尚未產生班表`
}
