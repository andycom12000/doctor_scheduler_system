/**
 * 違規 `code → 渲染` 對照表（issue #31）。純函式，不碰 DOM，靠 `violationStyle.test.ts` 守住。
 *
 * 規則（CLAUDE.md 任務描述逐字）：
 * - H1 空缺 → `--cell-vacancy-outline`（連同 `--cell-vacancy-bg` 的斜紋一起套用）
 * - H5 排到不可排班日 → `--cell-violation-stripe` 斜紋 + `--cell-violation-outline`
 * - 其他硬違規 → `--cell-violation-bg`
 * - 軟項不上格（回 `null`）
 *
 * 一個 cellKey 可能同時出現在多筆違規裡（例如 H2 與 H5 都是「已指派」的格子才會觸發，
 * 兩者可能同時命中同一格）；H1（沒有人）在定義上不可能與其他三種同時出現在同一格
 * （H1 恰好是「沒有人」，其餘三種都要求「已指派」），但仍給一個決定性的優先順序，
 * 不依賴違規清單的原始順序。
 */
import type { Severity, Violation } from '@/api/types'
import { parseCellKey } from './cellNav'
import { areaCellKey, staffCellKey } from './scheduleGrid'

export type CellRenderKind = 'vacancy' | 'violation-stripe' | 'violation-bg'

const H1_CODE = 'H1_AREA_COVERAGE'
const H5_CODE = 'H5_BLOCKED_DAY'

/** 優先度：數字愈大愈優先。 */
const PRIORITY: Record<CellRenderKind, number> = {
  'violation-stripe': 3,
  vacancy: 2,
  'violation-bg': 1,
}

/** 單一違規本身對應到哪種渲染，軟約束一律 `null`。 */
export function renderKindOf(code: string, severity: Severity): CellRenderKind | null {
  if (severity !== 'hard') return null
  if (code === H5_CODE) return 'violation-stripe'
  if (code === H1_CODE) return 'vacancy'
  return 'violation-bg'
}

/** 依「格子」索引出最終渲染種類；同一格多筆違規時取優先度最高者。 */
export function buildCellRenderIndex(violations: readonly Violation[]): Map<string, CellRenderKind> {
  const index = new Map<string, CellRenderKind>()
  for (const violation of violations) {
    const kind = renderKindOf(violation.code, violation.severity)
    if (!kind) continue
    for (const cellKey of violation.cellKeys) {
      const current = index.get(cellKey)
      if (!current || PRIORITY[kind] > PRIORITY[current]) {
        index.set(cellKey, kind)
      }
    }
  }
  return index
}

/**
 * 逐格違規大多用 `area:{areaId}:{date}` 記位置（H1、H2、H5……凡是「這一格排錯人」的規則）；
 * 日 × 人檢視是「人 × 日」的格線，同一個違規要轉成「當天在那個區值班的人」才畫得出來——
 * 這正是 PR #44 review 抓到的「H5 在日 × 人畫不出來」。H1（空缺）轉不出人，天生被排除
 * （`dutiesByArea` 查不到就跳過）。`staff:` 開頭的違規（H3／H4／H6／H7 本人累計型）
 * 已經是對的格子，原樣併入；兩邊命中同一格時一樣照 `PRIORITY` 取高的。
 */
export function projectRenderIndexToStaffView(
  index: ReadonlyMap<string, CellRenderKind>,
  dutiesByArea: ReadonlyMap<string, string>,
): Map<string, CellRenderKind> {
  const result = new Map<string, CellRenderKind>()
  const upsert = (key: string, kind: CellRenderKind): void => {
    const current = result.get(key)
    if (!current || PRIORITY[kind] > PRIORITY[current]) result.set(key, kind)
  }
  for (const [cellKey, kind] of index) {
    const parsed = parseCellKey(cellKey)
    if (!parsed) continue
    if (parsed.kind === 'staff') {
      upsert(cellKey, kind)
      continue
    }
    const staffId = dutiesByArea.get(`${parsed.id}|${parsed.date}`)
    if (!staffId) continue // H1 空缺或該格根本沒人值班：轉不出「那個人」，跳過
    upsert(staffCellKey(staffId, parsed.date), kind)
  }
  return result
}

/**
 * 反方向投影（PR #57 審查回饋 B1）：區域 × 日的右側欄限定只在這個分頁顯示，
 * `staff:` 開頭的違規（H3／H4／H6／H7 本人累計型）如果不投影回區域格，區域 × 日上完全不上色。
 * 用「當天在那個區值班」反查（`dutiesByStaff`：`staffId|date` → `areaId`）；查不到（理論上
 * 不該發生）就跳過。`area:` 開頭的違規原樣併入，同格取 `PRIORITY` 高者。
 */
export function projectRenderIndexToAreaView(
  index: ReadonlyMap<string, CellRenderKind>,
  dutiesByStaff: ReadonlyMap<string, string>,
): Map<string, CellRenderKind> {
  const result = new Map<string, CellRenderKind>()
  const upsert = (key: string, kind: CellRenderKind): void => {
    const current = result.get(key)
    if (!current || PRIORITY[kind] > PRIORITY[current]) result.set(key, kind)
  }
  for (const [cellKey, kind] of index) {
    const parsed = parseCellKey(cellKey)
    if (!parsed) continue
    if (parsed.kind === 'area') {
      upsert(cellKey, kind)
      continue
    }
    const areaId = dutiesByStaff.get(`${parsed.id}|${parsed.date}`)
    if (!areaId) continue // 查不到當天值班區域，轉不出格子，跳過
    upsert(areaCellKey(areaId, parsed.date), kind)
  }
  return result
}
