/**
 * 33 位醫師 + 1 位 NP。人數組成照 fixture 數值表：
 * R4×4、R5×5、R6×4、R2×3、R3×4、PGY1×2、PGY2×4、R1×4、PTR×3、NP×1。
 * 姓名為假名、員編 `E001…`、staff id `staff-001…`。
 */
import type { Staff } from '@/api/types'
import { eligibleAreaTypesOf } from './ranks'

interface StaffSeed {
  rankCode: string
  name: string
}

// 依序：PGY1×2、PGY2×4、R1×4、R2×3、R3×4、R4×4、R5×5、R6×4、PTR×3、NP×1 = 34 人。
const seeds: StaffSeed[] = [
  { rankCode: 'PGY1', name: '陳建宏' },
  { rankCode: 'PGY1', name: '林俊傑' },
  { rankCode: 'PGY2', name: '黃冠廷' },
  { rankCode: 'PGY2', name: '張承翰' },
  { rankCode: 'PGY2', name: '李柏翰' },
  { rankCode: 'PGY2', name: '王品睿' },
  { rankCode: 'R1', name: '吳宇軒' },
  { rankCode: 'R1', name: '劉冠宇' },
  { rankCode: 'R1', name: '蔡育誠' },
  { rankCode: 'R1', name: '楊家豪' },
  { rankCode: 'R2', name: '許志明' },
  { rankCode: 'R2', name: '鄭俊宏' },
  { rankCode: 'R2', name: '謝彥廷' },
  { rankCode: 'R3', name: '洪冠霖' },
  { rankCode: 'R3', name: '郭柏宇' },
  { rankCode: 'R3', name: '曾詩涵' },
  { rankCode: 'R3', name: '廖雅婷' },
  { rankCode: 'R4', name: '賴怡君' },
  { rankCode: 'R4', name: '徐淑芬' },
  { rankCode: 'R4', name: '周佳蓉' },
  { rankCode: 'R4', name: '葉淑惠' },
  { rankCode: 'R5', name: '蘇美玲' },
  { rankCode: 'R5', name: '莊靜怡' },
  { rankCode: 'R5', name: '呂雅雯' },
  { rankCode: 'R5', name: '江惠婷' },
  { rankCode: 'R5', name: '何心怡' },
  { rankCode: 'R6', name: '蕭佩珊' },
  { rankCode: 'R6', name: '羅郁婷' },
  { rankCode: 'R6', name: '高思妤' },
  { rankCode: 'R6', name: '潘芳瑜' },
  { rankCode: 'PTR', name: '簡婉婷' },
  { rankCode: 'PTR', name: '朱姿穎' },
  { rankCode: 'PTR', name: '鍾品妤' },
  { rankCode: 'NP', name: '游芷若' },
]

export function makeStaffFixture(): Staff[] {
  return seeds.map((seed, index) => {
    const n = index + 1
    const idSuffix = String(n).padStart(3, '0')
    return {
      id: `staff-${idSuffix}`,
      employeeNo: `E${idSuffix}`,
      name: seed.name,
      rankCode: seed.rankCode,
      status: 'active',
      eligibleAreaTypes: eligibleAreaTypesOf(seed.rankCode),
    }
  })
}
