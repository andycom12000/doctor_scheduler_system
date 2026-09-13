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
