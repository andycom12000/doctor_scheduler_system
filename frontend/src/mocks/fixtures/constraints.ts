/**
 * 7 硬 / 7 軟約束，逐字照 docs/constraint-defaults.md 的表。唯一預設值來源，
 * 不得另發明代碼或數字；改這裡前先改那份文件。
 */
import type { ConstraintSettings, HardConstraint, SoftConstraint } from '@/api/types'

const hard: HardConstraint[] = [
  {
    code: 'H1_AREA_COVERAGE',
    name: '每日每區恰好 1 人',
    primitive: 'ExactCount',
    enabled: true,
  },
  {
    code: 'H2_ELIGIBILITY',
    name: '身分資格',
    primitive: 'Eligible',
    enabled: true,
  },
  {
    code: 'H3_QUOTA_CAP',
    name: '額度點數上限',
    primitive: 'Budget',
    enabled: true,
    scope: { exemptRankCodes: ['NP'] },
    metric: 'quota_point',
  },
  {
    code: 'H4_MIN_GAP',
    name: '值休休值',
    primitive: 'MinGap',
    enabled: true,
    scope: { exemptRankCodes: ['NP'] },
    params: { days: 3 },
  },
  {
    code: 'H5_BLOCKED_DAY',
    name: '不可排班日',
    primitive: 'Forbidden',
    enabled: true,
  },
  {
    code: 'H6_NP_MONTHLY_DAYS',
    name: 'NP 每月天數上限',
    primitive: 'Budget',
    enabled: true,
    scope: { rankCodes: ['NP'] },
    metric: 'duty_day',
    params: { cap: 20 },
  },
  {
    code: 'H7_NP_MAX_CONSECUTIVE',
    name: 'NP 最多連六',
    primitive: 'MaxConsecutive',
    enabled: true,
    scope: { rankCodes: ['NP'] },
    params: { days: 6 },
  },
]

const soft: SoftConstraint[] = [
  {
    code: 'S1_QUOTA_FAIRNESS',
    name: '額度點數組內公平',
    primitive: 'Fairness',
    weight: 100,
    scope: { exemptRankCodes: ['NP'] },
    metric: 'quota_point',
  },
  {
    code: 'S2_AREA_CONSISTENCY',
    name: '同區延續',
    primitive: 'Consistency',
    weight: 40,
    scope: { exemptRankCodes: ['NP'] },
  },
  {
    code: 'S3_R2R3_PREFER_ICU',
    name: 'R2/R3 優先 ICU',
    primitive: 'Preference',
    weight: 50,
    scope: { rankCodes: ['R2', 'R3'], areaTypeCodes: ['ICU'] },
    params: { direction: 'prefer' },
  },
  {
    code: 'S4_R4R6_PREFER_CHIEF',
    name: 'R4~R6 優先總值',
    primitive: 'Preference',
    weight: 50,
    scope: { rankCodes: ['R4', 'R5', 'R6'], areaTypeCodes: ['CHIEF'] },
    params: { direction: 'prefer' },
  },
  {
    code: 'S5_NP_LAST_RESORT',
    name: 'NP 盡量不用',
    primitive: 'Preference',
    weight: 60,
    scope: { rankCodes: ['NP'] },
    params: { direction: 'avoid' },
  },
  {
    code: 'S6_NP_AVOID_HOLIDAY',
    name: 'NP 避開假日',
    primitive: 'Preference',
    weight: 30,
    scope: { rankCodes: ['NP'], dayKinds: ['holiday'] },
    params: { direction: 'avoid' },
  },
  {
    code: 'S7_FAIRNESS_POINT',
    name: '公平性點數組內公平',
    primitive: 'Fairness',
    weight: 0,
    scope: { exemptRankCodes: ['NP'] },
    metric: 'fairness_point',
  },
]

export const constraintSettings: ConstraintSettings = { hard, soft }

/** ADR-0003：三份變體固定的軟約束權重乘數。未列出的代碼乘數為 1。 */
export const variantWeightProfiles: Record<string, Record<string, number>> = {
  'v-a': { S1_QUOTA_FAIRNESS: 1.5, S2_AREA_CONSISTENCY: 0.5 },
  'v-b': { S1_QUOTA_FAIRNESS: 0.5, S2_AREA_CONSISTENCY: 1.5 },
  'v-c': {},
}

export const variantLabels: Record<string, { label: string; description: string }> = {
  'v-a': { label: '重視公平', description: '拉高額度點數組內公平的權重，壓低同區延續' },
  'v-b': { label: '重視延續性', description: '拉高同區延續的權重，壓低額度點數組內公平' },
  'v-c': { label: '平衡', description: '全部權重乘數為 1，即使用者在設定裡填的原始權重' },
}
