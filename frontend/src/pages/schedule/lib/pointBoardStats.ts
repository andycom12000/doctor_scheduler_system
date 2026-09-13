/**
 * 點數看板衍生的純函式：身分組容量利用率（組內 Σ已排 ÷ Σ上限）就地算，
 * 進 vitest（issue #31 任務描述）。
 */
import type { PointBoardGroup } from '@/api/types'

export interface GroupUtilization {
  groupCode: string
  groupName: string
  usedPoints: number
  capPoints: number | null
  /** 0–100，整數；`capPoints` 為 null（組內全是不計額度的人，如 NP）時也是 null。 */
  percent: number | null
}

/**
 * NP 這組的 `quotaCap` 全部是 `null`（不計），沒有分母可以算利用率，回 `percent: null`
 * 讓畫面顯示「不計」而不是除以零。其餘組全員都有上限，直接加總。
 */
export function computeGroupUtilization(groups: readonly PointBoardGroup[]): GroupUtilization[] {
  return groups.map((group) => {
    const capped = group.rows.filter((row) => row.quotaCap !== null)
    if (capped.length === 0) {
      return { groupCode: group.groupCode, groupName: group.groupName, usedPoints: 0, capPoints: null, percent: null }
    }
    const usedPoints = capped.reduce((sum, row) => sum + row.quotaPoints, 0)
    const capPoints = capped.reduce((sum, row) => sum + (row.quotaCap ?? 0), 0)
    const percent = capPoints > 0 ? Math.round((usedPoints / capPoints) * 100) : 0
    return { groupCode: group.groupCode, groupName: group.groupName, usedPoints, capPoints, percent }
  })
}
