/**
 * 點數看板衍生的純函式：身分組容量利用率（組內 Σ已排 ÷ Σ上限）就地算，
 * 進 vitest（issue #31 任務描述）；分組欄標題的「成員身分」清單（issue #53）。
 */
import type { PointBoardGroup } from '@/api/types'

/**
 * 分組欄標題的「組名 · 成員身分」格式（issue #53：「低年級 · PGY1 PGY2 R1 PTR」，
 * 不用 `groupCode` 的 G1／JUNIOR 這種內部代碼）。依 `group.rows` 出現順序去重—
 * 點數看板本來就列組內全部在職人員，第一次出現的身分順序即代表組內身分的排列順序。
 */
export function memberRankCodes(group: Pick<PointBoardGroup, 'rows'>): string {
  // 點數看板只列在職人員，這裡取到的一律是「本月在職名單」裡出現過的身分——已停用的人
  // 不在 `group.rows` 裡，不會混進這個清單（審查回饋 N4）。
  const seen = new Set<string>()
  const codes: string[] = []
  for (const row of group.rows) {
    if (seen.has(row.rankCode)) continue
    seen.add(row.rankCode)
    codes.push(row.rankCode)
  }
  return codes.join(' ')
}

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
