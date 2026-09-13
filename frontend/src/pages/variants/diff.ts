/**
 * 純函式：兩份變體的「逐格差異」。依 `areaId + date` 比對（`cellKey` 只是拿來
 * 顯示，不是比對鍵——見 CONTEXT.md「cellKey 是渲染層的索引鍵」）。
 */
import type { Duty } from '@/api/types'

export interface CellDiff {
  areaId: string
  date: string
  cellKey: string
  /** 第一份變體（`a`）在這一格的人；`null` 代表空缺。 */
  from: string | null
  /** 第二份變體（`b`）在這一格的人；`null` 代表空缺。 */
  to: string | null
}

function cellKeyOf(areaId: string, date: string): string {
  return `${areaId} ${date}`
}

function indexByCell(duties: Duty[]): Map<string, Duty> {
  const map = new Map<string, Duty>()
  for (const duty of duties) map.set(cellKeyOf(duty.areaId, duty.date), duty)
  return map
}

/** 只回傳兩邊指派不同的格子，依日期、區域排序。 */
export function diffVariants(a: Duty[], b: Duty[]): CellDiff[] {
  const mapA = indexByCell(a)
  const mapB = indexByCell(b)
  const keys = new Set([...mapA.keys(), ...mapB.keys()])

  const diffs: CellDiff[] = []
  for (const key of keys) {
    const dutyA = mapA.get(key)
    const dutyB = mapB.get(key)
    const from = dutyA?.staffId ?? null
    const to = dutyB?.staffId ?? null
    if (from === to) continue
    const sample = dutyA ?? dutyB
    if (!sample) continue
    diffs.push({
      areaId: sample.areaId,
      date: sample.date,
      cellKey: sample.cellKey ?? `area:${sample.areaId}:${sample.date}`,
      from,
      to,
    })
  }

  return diffs.sort((x, y) => (x.date === y.date ? x.areaId.localeCompare(y.areaId) : x.date.localeCompare(y.date)))
}

/**
 * 這份變體是不是目前草稿值班表已選定套用的那一份：逐格比對完全相同才算
 * （issue #54）。不看 `jobId`／`variantId` 之類的中繼資料——套用之後值班表
 * 可能又被逐格改過，比對值班內容本身才不會顯示「已選定」卻其實跟草稿不同。
 */
export function isVariantSelected(variantDuties: Duty[], scheduleDuties: Duty[]): boolean {
  // 變體理論上不會是空陣列，但守一下：兩邊都空時不算「已選定」，避免尚未載入完成的
  // 草稿誤判成套用過。
  if (variantDuties.length === 0) return false
  return diffVariants(variantDuties, scheduleDuties).length === 0
}
