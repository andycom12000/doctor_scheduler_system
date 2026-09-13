import { describe, expect, it } from 'vitest'
import type { SoftConstraint } from '@/api/types'
import {
  accumulateElapsed,
  assignmentCount,
  acceptPolledSnapshot,
  averagePerVariantSeconds,
  buildMetricRows,
  describeFailure,
  disabledSoftCodes,
  formatDisabledSuffix,
  formatGap,
  formatMetricValue,
  formatMultipliers,
  formatSeconds,
  initialElapsedAccumulator,
  isFairnessPointEnabled,
  isTerminalStatus,
  progressHeadline,
  shortJobId,
  stanceLine,
  totalElapsedSec,
  variantSlotStatuses,
  variantTitle,
} from './variantView'

describe('shortJobId', () => {
  it('去掉 job- 前綴，取前 4 碼並轉大寫', () => {
    expect(shortJobId('job-3e55a1b2c3d4')).toBe('3E55')
  })
  it('沒有 job- 前綴也能處理', () => {
    expect(shortJobId('7k3xabc')).toBe('7K3X')
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
  it('null 顯示 em dash', () => {
    expect(formatGap(null)).toBe('—')
    expect(formatGap(undefined)).toBe('—')
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
  it('真後端的 gap 可能 > 1，一律封頂顯示 >100%', () => {
    expect(formatGap(1.2986)).toBe('>100%')
  })
  it('gap 剛好等於 1 仍視為百分比', () => {
    expect(formatGap(1)).toBe('100%')
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

describe('assignmentCount', () => {
  it('指派格數是區 × 日，不是契約的 variables', () => {
    expect(assignmentCount({ areas: 5, days: 30 })).toBe(150)
    expect(assignmentCount(null)).toBeNull()
    expect(assignmentCount(undefined)).toBeNull()
    expect(assignmentCount({ areas: 5 })).toBeNull()
  })
})

describe('variantTitle', () => {
  it('由 v-a/v-b/v-c 推出「變體 A／B／C」', () => {
    expect(variantTitle('v-a')).toBe('變體 A')
    expect(variantTitle('v-b')).toBe('變體 B')
    expect(variantTitle('v-c')).toBe('變體 C')
  })
})

describe('formatMetricValue', () => {
  it('空缺數帶「格」', () => {
    expect(formatMetricValue('vacancies', 0)).toBe('0 格')
  })
  it('額度點數公平與公平性點數帶 Δ 與「點」', () => {
    expect(formatMetricValue('quotaFairness', 2)).toBe('Δ 2 點')
    expect(formatMetricValue('fairnessPoint', 5)).toBe('Δ 5 點')
  })
  it('同區延續帶「次跨區」', () => {
    expect(formatMetricValue('areaConsistency', 32)).toBe('32 次跨區')
  })
  it('身分區域偏好是未滿足次數，帶「次」而非契約沒有的百分比', () => {
    expect(formatMetricValue('rankPreference', 1)).toBe('1 次')
  })
})

describe('disabledSoftCodes／formatDisabledSuffix', () => {
  const base: SoftConstraint = { code: 'S1_QUOTA_FAIRNESS', name: '額度點數公平', primitive: 'Fairness', weight: 10 }

  it('權重 0 視為停用，取代碼前綴', () => {
    const soft: SoftConstraint[] = [base, { ...base, code: 'S7_FAIRNESS_POINT', weight: 0 }]
    expect(disabledSoftCodes(soft)).toEqual(['S7'])
    expect(formatDisabledSuffix(disabledSoftCodes(soft))).toBe('（S7 停用）')
  })
  it('沒有停用的條目時回空陣列與空字串', () => {
    expect(disabledSoftCodes([base])).toEqual([])
    expect(formatDisabledSuffix([])).toBe('')
  })
  it('undefined 視為沒有設定', () => {
    expect(disabledSoftCodes(undefined)).toEqual([])
  })
})

describe('stanceLine', () => {
  it('第 1 份是重視公平，列出乘數', () => {
    expect(stanceLine(1)).toBe('立場：重視公平（S1 ×1.5 · S2 ×0.5）')
  })
  it('第 2 份是重視延續性', () => {
    expect(stanceLine(2)).toBe('立場：重視延續性（S1 ×0.5 · S2 ×1.5）')
  })
  it('第 3 份是平衡，乘數全部為 1 時顯示「全部 ×1」', () => {
    expect(stanceLine(3)).toBe('立場：平衡（全部 ×1）')
  })
  it('排隊中（variantIndex 0）回 null，不顯示這一行', () => {
    expect(stanceLine(0)).toBeNull()
  })
})

describe('averagePerVariantSeconds', () => {
  it('契約沒有每份變體的耗時，退回整體耗時／份數', () => {
    expect(averagePerVariantSeconds(14.7, 3)).toBeCloseTo(4.9)
  })
  it('整體耗時為 null 時回 null', () => {
    expect(averagePerVariantSeconds(null, 3)).toBeNull()
    expect(averagePerVariantSeconds(undefined, 3)).toBeNull()
  })
  it('份數為 0 時回 null，不除以 0', () => {
    expect(averagePerVariantSeconds(10, 0)).toBeNull()
  })
})

describe('elapsed 累加器', () => {
  it('同一份變體內，用最新事件的耗時取代顯示值', () => {
    let acc = initialElapsedAccumulator(0, 0, 0)
    acc = accumulateElapsed(acc, { variantIndex: 1, elapsedSec: 3 })
    expect(totalElapsedSec(acc)).toBe(3)
    acc = accumulateElapsed(acc, { variantIndex: 1, elapsedSec: 7 })
    expect(totalElapsedSec(acc)).toBe(7)
  })
  it('換下一份變體時，把上一份最後看到的耗時併入基底', () => {
    let acc = initialElapsedAccumulator(0, 0, 0)
    acc = accumulateElapsed(acc, { variantIndex: 1, elapsedSec: 7 })
    acc = accumulateElapsed(acc, { variantIndex: 2, elapsedSec: 1 })
    expect(totalElapsedSec(acc)).toBe(8)
  })
  it('重新接上一個進行中的工作：用整體耗時扣掉本份耗時當基底，不會跟目前這份重複計算', () => {
    const acc = initialElapsedAccumulator(20, 2, 5)
    expect(totalElapsedSec(acc)).toBe(20)
    const next = accumulateElapsed(acc, { variantIndex: 2, elapsedSec: 8 })
    expect(totalElapsedSec(next)).toBe(23)
  })
  it('同一份變體內耗時倒退（多樣性重試重新 BeginVariant）也視為新一段，不讓總耗時退回去', () => {
    let acc = initialElapsedAccumulator(0, 0, 0)
    acc = accumulateElapsed(acc, { variantIndex: 1, elapsedSec: 12 })
    acc = accumulateElapsed(acc, { variantIndex: 1, elapsedSec: 0.3 })
    expect(totalElapsedSec(acc)).toBeCloseTo(12.3)
  })
})
