/**
 * 純函式：把 `SolverJob` / `Variant` 的原始欄位轉成畫面要顯示的字串與狀態，
 * 跟元件本身分開才能不掛 DOM 進 vitest（README／frontend-plan.md §5）。
 */
import type { SoftConstraint, SolverJobStatus, Variant } from '@/api/types'

/** 標題列的短編號，前 8 碼。 */
export function shortJobId(jobId: string): string {
  return jobId.slice(0, 8)
}

export function formatSeconds(value: number | null | undefined): string {
  if (value == null) return '—'
  return `${value.toFixed(1)}s`
}

/**
 * 收斂間隙。**只能當數字讀，不可畫成進度條**（ARCHITECTURE §4.7）。
 * `null` 代表還沒找到第一個可行解。
 *
 * 後端 `SolverJobService.GapOf` 回的是比例（`|obj-bound|/|obj|`，四捨五入到小數 4 位，
 * 例如 0.0287），不是百分比，所以這裡要 ×100 才是「gap 2.87%」。
 * MSW mock（`src/mocks/handlers.ts`）目前塞的是 `5 / i` 這種未除以 100 的假數字，
 * 只是拿來湊「會上下跳動」的觀感，跟真正的公式對不上——這裡照真後端的單位寫，
 * mock 顯示出來的百分比會偏大，見 PR 說明。
 */
export function formatGap(gap: number | null | undefined): string {
  if (gap == null) return '尚無可行解'
  return `${Number((gap * 100).toFixed(2))}%`
}

/** 只列出乘數 ≠ 1 的軟約束，格式「S2 ×1.5」。 */
export function formatMultipliers(weightProfile: Record<string, number> | undefined): { code: string; text: string }[] {
  if (!weightProfile) return []
  return Object.entries(weightProfile)
    .filter(([, multiplier]) => multiplier !== 1)
    .map(([code, multiplier]) => ({ code, text: `${code.split('_')[0]} ×${multiplier}` }))
}

export interface MetricRow {
  key: string
  label: string
  value: number
}

const METRIC_LABELS: Record<string, string> = {
  vacancies: '空缺',
  quotaFairness: '額度點數公平',
  areaConsistency: '同區延續',
  rankPreference: '身分區域偏好',
  fairnessPoint: '公平性點數',
}

/**
 * 指標列固定 4 個；`fairnessPoint` 非 null 且 S7 權重 > 0 時才加第 5 個
 * （frontend-plan.md §3.5）。
 */
export function buildMetricRows(metrics: Variant['metrics'], showFairnessPoint: boolean): MetricRow[] {
  const rows: MetricRow[] = [
    { key: 'vacancies', label: METRIC_LABELS.vacancies, value: metrics?.vacancies ?? 0 },
    { key: 'quotaFairness', label: METRIC_LABELS.quotaFairness, value: metrics?.quotaFairness ?? 0 },
    { key: 'areaConsistency', label: METRIC_LABELS.areaConsistency, value: metrics?.areaConsistency ?? 0 },
    { key: 'rankPreference', label: METRIC_LABELS.rankPreference, value: metrics?.rankPreference ?? 0 },
  ]
  if (showFairnessPoint && metrics?.fairnessPoint != null) {
    rows.push({ key: 'fairnessPoint', label: METRIC_LABELS.fairnessPoint, value: metrics.fairnessPoint })
  }
  return rows
}

/** S7（公平性點數組內公平）是否開著，決定第 5 個指標列要不要顯示。 */
export function isFairnessPointEnabled(soft: SoftConstraint[] | undefined): boolean {
  return (soft ?? []).some((c) => c.code === 'S7_FAIRNESS_POINT' && c.weight > 0)
}

export type VariantSlotStatus = 'done' | 'running' | 'waiting'

/**
 * 三份變體依 `variantIndex`（1-based，0 代表排隊中還沒開始）推出的狀態列。
 * `variantIndex` 之前的份數視為完成，正在跑的那一份是 running，其餘 waiting。
 */
export function variantSlotStatuses(variantCount: number, variantIndex: number): VariantSlotStatus[] {
  return Array.from({ length: Math.max(variantCount, 0) }, (_, i) => {
    const n = i + 1
    if (n < variantIndex) return 'done'
    if (n === variantIndex) return 'running'
    return 'waiting'
  })
}

/** ADR-0003 固定的變體 id 順序，只用來標三個狀態格（`v-a 完成` 這類短標籤）。 */
export const VARIANT_SLOT_IDS = ['v-a', 'v-b', 'v-c'] as const

export function progressHeadline(status: SolverJobStatus, variantIndex: number, variantCount: number): string {
  if (status === 'queued' || variantIndex < 1) return '排隊中'
  return `正在求第 ${variantIndex} / ${variantCount} 份變體`
}

/** 程式重啟中斷是最常見的失敗原因，文案要讓使用者知道重新求解即可（ARCHITECTURE §4.8）。 */
export function describeFailure(failureReason: string | null | undefined): string {
  if (!failureReason) return ''
  return failureReason.includes('重啟') ? `${failureReason}，重新求解即可。` : failureReason
}

export function isTerminalStatus(status: SolverJobStatus): boolean {
  return status === 'succeeded' || status === 'failed' || status === 'cancelled'
}
