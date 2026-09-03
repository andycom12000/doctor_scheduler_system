/**
 * 兩套點數的出廠規則。額度點數 weekday 1 / holiday 2；
 * 公平性點數表 A / B；連值週六加分。逐字照 api-contract.yaml 的範例
 * 與 docs/constraint-defaults.md 的度量定義。
 */
import type { PointRules } from '@/api/types'

export const pointRules: PointRules = {
  quota: {
    weekday: 1,
    holiday: 2,
  },
  fairness: {
    tables: {
      A: [
        { today: 'holiday', tomorrow: 'holiday', points: 3 },
        { today: 'holiday', tomorrow: 'weekday', points: 2 },
        { today: 'weekday', tomorrow: 'holiday', points: 2 },
        { today: 'weekday', tomorrow: 'weekday', points: 1 },
      ],
      B: [
        { today: 'holiday', tomorrow: 'holiday', points: 2 },
        { today: 'holiday', tomorrow: 'weekday', points: 3 },
        { today: 'weekday', tomorrow: 'holiday', points: 1 },
        { today: 'weekday', tomorrow: 'weekday', points: 2 },
      ],
    },
    consecutiveSaturdayBonus: {
      points: 1,
      windowDays: 10,
    },
  },
}
