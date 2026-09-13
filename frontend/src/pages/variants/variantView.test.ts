import { describe, expect, it } from 'vitest'
import type { SoftConstraint } from '@/api/types'
import {
  acceptPolledSnapshot,
  buildMetricRows,
  describeFailure,
  formatGap,
  formatMultipliers,
  formatSeconds,
  isFairnessPointEnabled,
  isTerminalStatus,
  progressHeadline,
  shortJobId,
  variantSlotStatuses,
} from './variantView'

describe('shortJobId', () => {
  it('取前 8 碼', () => {
    expect(shortJobId('job-0000000000001')).toBe('job-0000')
  })
})

describe('formatSeconds', () => {
  it('null 顯示 em dash', () => {
    expect(formatSeconds(null)).toBe('—')
    expect(formatSeconds(undefined)).toBe('—')
  })
  it('保留一位小數並加 s', () => {
    expect(formatSeconds(8.42)).toBe('8.4s')
  })
})

describe('formatGap', () => {
  it('null 顯示「尚無可行解」，不是 0 或空字串', () => {
    expect(formatGap(null)).toBe('尚無可行解')
    expect(formatGap(undefined)).toBe('尚無可行解')
  })
  it('0 代表已證明最佳，仍是數字', () => {
    expect(formatGap(0)).toBe('0%')
  })
  it('是純數字文字，不夾帶長條或百分比以外的符號', () => {
    expect(formatGap(0.025)).toBe('2.5%')
  })
  it('後端回的是比例（GapOf），這裡轉成百分比要 ×100', () => {
    expect(formatGap(0.0287)).toBe('2.87%')
  })
})

describe('formatMultipliers', () => {
  it('只列乘數不是 1 的軟約束，格式 S{n} ×{multiplier}', () => {
    const result = formatMultipliers({ S1_QUOTA_FAIRNESS: 1.5, S2_AREA_CONSISTENCY: 0.5 })
    expect(result).toEqual([
      { code: 'S1_QUOTA_FAIRNESS', text: 'S1 ×1.5' },
      { code: 'S2_AREA_CONSISTENCY', text: 'S2 ×0.5' },
    ])
  })

  it('平衡變體（全部為 1）回傳空陣列', () => {
    expect(formatMultipliers({ S1_QUOTA_FAIRNESS: 1, S2_AREA_CONSISTENCY: 1 })).toEqual([])
  })

  it('undefined 回傳空陣列', () => {
    expect(formatMultipliers(undefined)).toEqual([])
  })
})

describe('buildMetricRows', () => {
  const metrics = { vacancies: 2, quotaFairness: 4, areaConsistency: 6, rankPreference: 1, fairnessPoint: 12 }

  it('固定 4 個指標，S7 關閉時不含公平性點數', () => {
    const rows = buildMetricRows(metrics, false)
    expect(rows).toHaveLength(4)
    expect(rows.map((r) => r.key)).toEqual(['vacancies', 'quotaFairness', 'areaConsistency', 'rankPreference'])
  })

  it('S7 開啟且 fairnessPoint 非 null 時多一列', () => {
    const rows = buildMetricRows(metrics, true)
    expect(rows).toHaveLength(5)
    expect(rows[4]).toEqual({ key: 'fairnessPoint', label: '公平性點數', value: 12 })
  })

  it('S7 開啟但 fairnessPoint 為 null 時仍是 4 個', () => {
    const rows = buildMetricRows({ ...metrics, fairnessPoint: null }, true)
    expect(rows).toHaveLength(4)
  })
})

describe('isFairnessPointEnabled', () => {
  const base: SoftConstraint = { code: 'S7_FAIRNESS_POINT', name: '公平性點數組內公平', primitive: 'Fairness', weight: 0 }

  it('S7 權重 0 視為停用', () => {
    expect(isFairnessPointEnabled([base])).toBe(false)
  })
  it('S7 權重 > 0 視為啟用', () => {
    expect(isFairnessPointEnabled([{ ...base, weight: 10 }])).toBe(true)
  })
  it('找不到 S7 視為停用', () => {
    expect(isFairnessPointEnabled([])).toBe(false)
    expect(isFairnessPointEnabled(undefined)).toBe(false)
  })
})

describe('variantSlotStatuses', () => {
  it('排隊中（variantIndex 0）全部 waiting', () => {
    expect(variantSlotStatuses(3, 0)).toEqual(['waiting', 'waiting', 'waiting'])
  })
  it('正在求第 2 份：第 1 份完成、第 2 份求解中、第 3 份等待', () => {
    expect(variantSlotStatuses(3, 2)).toEqual(['done', 'running', 'waiting'])
  })
  it('只求 1 份時陣列長度為 1', () => {
    expect(variantSlotStatuses(1, 1)).toEqual(['running'])
  })
})

describe('progressHeadline', () => {
  it('queued 顯示排隊中', () => {
    expect(progressHeadline('queued', 0, 3)).toBe('排隊中')
  })
  it('running 顯示第 n / N 份', () => {
    expect(progressHeadline('running', 2, 3)).toBe('正在求第 2 / 3 份變體')
  })
})

describe('describeFailure', () => {
  it('空字串或 null 回空字串', () => {
    expect(describeFailure(null)).toBe('')
    expect(describeFailure(undefined)).toBe('')
    expect(describeFailure('')).toBe('')
  })
  it('包含「重啟」字樣時附加重新求解提示', () => {
    expect(describeFailure('程式重啟中斷')).toBe('程式重啟中斷，重新求解即可。')
  })
  it('其他原因照原樣顯示', () => {
    expect(describeFailure('內部錯誤')).toBe('內部錯誤')
  })
})

describe('isTerminalStatus', () => {
  it('succeeded／failed／cancelled 是終態', () => {
    expect(isTerminalStatus('succeeded')).toBe(true)
    expect(isTerminalStatus('failed')).toBe(true)
    expect(isTerminalStatus('cancelled')).toBe(true)
  })
  it('queued／running 不是終態', () => {
    expect(isTerminalStatus('queued')).toBe(false)
    expect(isTerminalStatus('running')).toBe(false)
  })
})

describe('acceptPolledSnapshot', () => {
  it('目前非終態時接受輪詢快照（不論快照本身的狀態）', () => {
    expect(acceptPolledSnapshot('running')).toBe(true)
    expect(acceptPolledSnapshot('queued')).toBe(true)
  })
  it('終態是單向門：目前已是終態就一律拒絕，避免較舊的輪詢把畫面退回去', () => {
    expect(acceptPolledSnapshot('succeeded')).toBe(false)
    expect(acceptPolledSnapshot('failed')).toBe(false)
    expect(acceptPolledSnapshot('cancelled')).toBe(false)
  })
})
