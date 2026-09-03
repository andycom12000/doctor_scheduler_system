/**
 * 3 種區域類型 + 5 個區域。逐字照 CLAUDE_TASK 的 fixture 數值表，
 * 不得另發明代碼或數字。
 */
import type { Area, AreaType } from '@/api/types'

export const areaTypes: AreaType[] = [
  { code: 'WARD', name: '一般病房' },
  { code: 'ICU', name: '加護病房' },
  { code: 'CHIEF', name: '總值' },
]

export const areas: Area[] = [
  { id: 'area-a', code: 'A', name: 'A', areaTypeCode: 'WARD', requiredPerDay: 1 },
  { id: 'area-b', code: 'B', name: 'B', areaTypeCode: 'WARD', requiredPerDay: 1 },
  { id: 'area-c', code: 'C', name: 'C', areaTypeCode: 'WARD', requiredPerDay: 1 },
  { id: 'area-icu', code: 'ICU', name: 'ICU', areaTypeCode: 'ICU', requiredPerDay: 1 },
  { id: 'area-chief', code: 'CHIEF', name: '總值', areaTypeCode: 'CHIEF', requiredPerDay: 1 },
]

/** 求解／填格時固定的區域走訪順序：資格窄的先填，較容易早發現無解。 */
export const areaFillOrder = ['area-chief', 'area-icu', 'area-a', 'area-b', 'area-c']
