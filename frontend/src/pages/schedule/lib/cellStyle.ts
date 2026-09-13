/**
 * 格子視覺狀態的純函式（PR #44 審查修正）。狀態優先序統一寫在這裡一次，
 * `AreaByDayGrid`／`DayByStaffGrid` 的 `cellClass()` 只把這裡回傳的 `CellKind`
 * 對到自己那份 BEM class，不再各自兜字串鍵——上一輪就是死在「component 自己組的
 * 鍵跟 CSS 選擇器對不起來」這層，這裡補 vitest 釘住優先序，不能再靠肉眼核對字串。
 */
import type { CellRenderKind } from './violationStyle'

export type CellKind =
  | 'violation-stripe'
  | 'violation-bg'
  | 'vacancy'
  | 'blocked'
  | 'duty'
  | 'duty-holiday'
  | 'holiday'
  | 'empty'

export interface CellStyleInput {
  hasDuty: boolean
  isHoliday: boolean
  /** 這一格算出來的違規渲染種類；沒有硬違規（或違規是軟項）時是 `null`。 */
  renderKind: CellRenderKind | null
  /** 是否命中不可排班日登記（`blocked-days`）。只有日 × 人檢視會傳 `true`——區域 × 日不畫這個狀態。 */
  isBlocked?: boolean
}

/**
 * 優先序（高到低）：違規斜紋／底色 → 空缺 → 不可排班日登記 → 值班（假日再疊一層）→
 * 假日空格 → 一般空格。
 *
 * 違規一定蓋過登記本身——H5（排到已登記的不可排班日）就是「登記 + 值班」撞在一起的
 * 結果，同一格畫兩種樣式沒有意義，斜紋已經講完整個故事。
 */
export function cellKindOf({ hasDuty, isHoliday, renderKind, isBlocked = false }: CellStyleInput): CellKind {
  if (renderKind === 'violation-stripe') return 'violation-stripe'
  if (renderKind === 'violation-bg') return 'violation-bg'
  if (renderKind === 'vacancy') return 'vacancy'
  if (isBlocked) return 'blocked'
  if (hasDuty) return isHoliday ? 'duty-holiday' : 'duty'
  return isHoliday ? 'holiday' : 'empty'
}

/**
 * 4 身分組色階 `--group-1..4`：依組在點數看板裡實際出現的順序分配（0-based → 1-based），
 * 不寫死組代碼（JUNIOR／MID／SENIOR／NP 是這份 fixture 的值，不是規則）。
 */
export function groupColorIndex(groupIndex: number): 1 | 2 | 3 | 4 {
  return ((((groupIndex % 4) + 4) % 4) + 1) as 1 | 2 | 3 | 4
}
