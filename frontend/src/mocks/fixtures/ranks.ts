/**
 * 10 身分 + 4 身分組 + 資格矩陣。逐字照 CLAUDE_TASK 的 fixture 數值表：
 *
 * | code | groupCode | WARD | ICU | CHIEF | quotaCap | pointType |
 * |---|---|---|---|---|---|---|
 * | PGY1 | JUNIOR | ✓ ✗ ✗ | 10 | A |
 * | PGY2 | JUNIOR | ✓ ✗ ✗ |  9 | A |
 * | R1   | JUNIOR | ✓ ✗ ✗ |  9 | A |
 * | R2   | MID    | ✓ ✓ ✗ |  8 | A |
 * | R3   | MID    | ✓ ✓ ✗ |  7 | A |
 * | R4   | SENIOR | ✗ ✓ ✓ |  6 | B |
 * | R5   | SENIOR | ✗ ✓ ✓ |  5 | B |
 * | R6   | SENIOR | ✗ ✓ ✓ |  5（預設，monthly-overrides 可覆寫） | B |
 * | PTR  | JUNIOR | ✓ ✗ ✗ |  6 | A |
 * | NP   | NP     | ✓ ✗ ✗ | null | null |
 */
import type { EligibilityMatrix, Rank, RankGroup } from '@/api/types'

export const rankGroups: RankGroup[] = [
  { code: 'JUNIOR', name: '低年級' },
  { code: 'MID', name: '中階' },
  { code: 'SENIOR', name: '資深' },
  { code: 'NP', name: 'NP' },
]

export const ranks: Rank[] = [
  { code: 'PGY1', name: 'PGY1', groupCode: 'JUNIOR', quotaCap: 10, pointType: 'A' },
  { code: 'PGY2', name: 'PGY2', groupCode: 'JUNIOR', quotaCap: 9, pointType: 'A' },
  { code: 'R1', name: 'R1', groupCode: 'JUNIOR', quotaCap: 9, pointType: 'A' },
  { code: 'R2', name: 'R2', groupCode: 'MID', quotaCap: 8, pointType: 'A' },
  { code: 'R3', name: 'R3', groupCode: 'MID', quotaCap: 7, pointType: 'A' },
  { code: 'R4', name: 'R4', groupCode: 'SENIOR', quotaCap: 6, pointType: 'B' },
  { code: 'R5', name: 'R5', groupCode: 'SENIOR', quotaCap: 5, pointType: 'B' },
  { code: 'R6', name: 'R6', groupCode: 'SENIOR', quotaCap: 5, pointType: 'B' },
  { code: 'PTR', name: '打工R', groupCode: 'JUNIOR', quotaCap: 6, pointType: 'A' },
  { code: 'NP', name: 'NP', groupCode: 'NP', quotaCap: null, pointType: null },
]

export const eligibilityMatrix: EligibilityMatrix = {
  matrix: {
    PGY1: { WARD: true, ICU: false, CHIEF: false },
    PGY2: { WARD: true, ICU: false, CHIEF: false },
    R1: { WARD: true, ICU: false, CHIEF: false },
    R2: { WARD: true, ICU: true, CHIEF: false },
    R3: { WARD: true, ICU: true, CHIEF: false },
    R4: { WARD: false, ICU: true, CHIEF: true },
    R5: { WARD: false, ICU: true, CHIEF: true },
    R6: { WARD: false, ICU: true, CHIEF: true },
    PTR: { WARD: true, ICU: false, CHIEF: false },
    NP: { WARD: true, ICU: false, CHIEF: false },
  },
}

/**
 * 由資格矩陣依 rankCode 推導可值區域類型；Staff.eligibleAreaTypes 唯讀，由此算出。
 *
 * **矩陣由呼叫端傳入，不讀這個模組的常數** `eligibilityMatrix`——那份只是出廠預設值。
 * `PUT /api/settings/eligibility-matrix` 會改動 `store.eligibilityMatrix`，
 * 若這裡改讀模組常數，矩陣更新後只有 H2 檢查看得到新資料，
 * `GET /api/staff`／候選人／可行性／產生器全部還在用舊矩陣，會互相矛盾。
 */
export function eligibleAreaTypesOf(matrix: EligibilityMatrix['matrix'], rankCode: string): string[] {
  const row = matrix[rankCode] ?? {}
  return Object.entries(row)
    .filter(([, ok]) => ok)
    .map(([areaTypeCode]) => areaTypeCode)
}
