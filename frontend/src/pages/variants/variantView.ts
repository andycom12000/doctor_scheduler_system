/**
 * 純函式：把 `SolverJob` / `Variant` 的原始欄位轉成畫面要顯示的字串與狀態，
 * 跟元件本身分開才能不掛 DOM 進 vitest（README／frontend-plan.md §5）。
 */
import type { SoftConstraint, SolverJobStatus, Variant } from '@/api/types'

/**
 * 標題列的短編號：去掉 `job-` 前綴後取前 4 碼、轉大寫（issue #54）。
 * 例：`job-3e55a1b2c3d4` → `3E55`。呼叫端自己補 `#` 前綴。
 */
export function shortJobId(jobId: string): string {
  return jobId.replace(/^job-/, '').slice(0, 4).toUpperCase()
}

export function formatSeconds(value: number | null | undefined): string {
  if (value == null) return '—'
  return `${value.toFixed(1)}s`
}

/**
 * 收斂間隙。**只能當數字讀，不可畫成進度條**（ARCHITECTURE §4.7）。
 * `null` 代表還沒找到第一個可行解，顯示 em dash。
 *
 * 後端 `SolverJobService.GapOf` 回的是比例（`|obj-bound|/|obj|`），不是百分比，
 * 所以要 ×100 才是「gap 2.87%」；真後端這個比例可能 > 1（分母 `|obj|` 很小時），
 * 一律封頂顯示「>100%」，不然畫面會出現「129.86%」這種不像收斂度的數字（issue #54）。
 */
export function formatGap(gap: number | null | undefined): string {
  if (gap == null) return '—'
  if (gap > 1) return '>100%'
  return `${Number((gap * 100).toFixed(2))}%`
}

/** 只列出乘數 ≠ 1 的軟約束，格式「S2 ×1.5」。 */
export function formatMultipliers(weightProfile: Record<string, number> | undefined): { code: string; text: string }[] {
  if (!weightProfile) return []
  return Object.entries(weightProfile)
    .filter(([, multiplier]) => multiplier !== 1)
    .map(([code, multiplier]) => ({ code, text: `${code.split('_')[0]} ×${multiplier}` }))
}

/**
 * 卡片標題「變體 A／B／C」，由 `variant.id`（ADR-0003 固定的 `v-a`/`v-b`/`v-c`）推出。
 * `variant.label` 是立場名稱（「重視公平」），設計稿把它另外放進晶片，不當標題用。
 */
export function variantTitle(variantId: string): string {
  const letter = variantId.split('-').at(-1) ?? variantId
  return `變體 ${letter.toUpperCase()}`
}

/**
 * 指標數值的單位，依 `api-contract.yaml` 的 `VariantMetrics` 描述與 `CONTEXT.md`：
 * - `vacancies`：未填的格子數 → 「格」
 * - `quotaFairness`：組內剩餘額度 `max − min` 的總和 → 「Δ N 點」（額度點數）
 * - `areaConsistency`：離開主區的次數總和 → 「N 次跨區」
 * - `rankPreference`：偏好**未滿足次數**（整數，越小越好）→ 「N 次」。
 *   設計稿的 canvas mock（`docs/design-ref/canvas-data.js`）畫的是虛構的「ICU 配置滿足率 93%」，
 *   但契約給的是原始未滿足次數、沒有分母湊不出比率，這裡照契約用次數，不臆造百分比（issue #54 PR 說明）。
 * - `fairnessPoint`：與 `quotaFairness` 同樣是組內 `max − min` 的總和 → 「Δ N 點」
 */
export function formatMetricValue(key: string, value: number): string {
  switch (key) {
    case 'vacancies':
      return `${value} 格`
    case 'quotaFairness':
    case 'fairnessPoint':
      return `Δ ${value} 點`
    case 'areaConsistency':
      return `${value} 次跨區`
    case 'rankPreference':
      return `${value} 次`
    default:
      return `${value}`
  }
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

/**
 * 權重 0 視為停用的軟約束短碼（`S1`、`S7` 這種前綴），供標題橫幅的
 * 「軟約束 6（S7 停用）」附註使用。`SolverJob.constraintCount.soft` 本身已經是
 * active 的數量（後端 `ConstraintCount` 只算 `IsActive`），這裡另外算是為了列出
 * *哪幾條* 被停用，契約沒有這個欄位，是從 `GET /settings/constraints` 現有的
 * 那份設定推出來的（issue #54 PR 說明）。
 */
export function disabledSoftCodes(soft: SoftConstraint[] | undefined): string[] {
  return (soft ?? []).filter((c) => c.weight === 0).map((c) => c.code.split('_')[0])
}

/** 「軟約束 6（S7 停用）」這類附註；沒有停用的條目時回空字串。 */
export function formatDisabledSuffix(codes: string[]): string {
  return codes.length ? `（${codes.join('、')} 停用）` : ''
}

/**
 * ADR-0003／`docs/constraint-defaults.md`「變體的權重乘數」固定的三個具名立場，
 * 依 `variantIndex`（1-based）查表。契約的 `SolverProgress` 只給 `variantIndex`，
 * 不會在求解中途回這份變體的 `label`／`weightProfile`（那是 `Variant`，變體完成後才有），
 * 所以 04b 進度覆蓋層的「立場」行只能照抄這份寫死在後端的對照表，不是從 API 讀來的。
 */
const VARIANT_STANCE_PROFILES: { id: string; label: string; multipliers: Record<string, number> }[] = [
  { id: 'v-a', label: '重視公平', multipliers: { S1_QUOTA_FAIRNESS: 1.5, S2_AREA_CONSISTENCY: 0.5 } },
  { id: 'v-b', label: '重視延續性', multipliers: { S1_QUOTA_FAIRNESS: 0.5, S2_AREA_CONSISTENCY: 1.5 } },
  { id: 'v-c', label: '平衡', multipliers: {} },
]

/** 「立場：重視延續性（S1 ×0.5 · S2 ×1.5）」；`variantIndex < 1`（排隊中還沒開始）回 `null`。 */
export function stanceLine(variantIndex: number): string | null {
  const profile = VARIANT_STANCE_PROFILES[variantIndex - 1]
  if (!profile) return null
  const multipliers = formatMultipliers(profile.multipliers)
  const multiplierText = multipliers.length ? multipliers.map((m) => m.text).join(' · ') : '全部 ×1'
  return `立場：${profile.label}（${multiplierText}）`
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

/**
 * 看門狗輪詢（issue #46）與 SSE 進度可能交錯抵達：一支較舊的輪詢回應如果在
 * 終態已經確立之後才落地，不可以把畫面退回非終態。**終態是單向門**——
 * 目前已是終態就一律拒絕新快照，不比對時間戳（後端沒給、前端也不用管）。
 */
export function acceptPolledSnapshot(currentStatus: SolverJobStatus): boolean {
  return !isTerminalStatus(currentStatus)
}

/**
 * 契約沒有「每份變體的耗時」欄位（`Variant` 只有整份工作的 `weightProfile`／`metrics`），
 * 卡片右上角只能退回「整體耗時／份數」的平均值，PR 說明列出這個近似（issue #54）。
 */
export function averagePerVariantSeconds(totalElapsedSec: number | null | undefined, variantCount: number): number | null {
  if (totalElapsedSec == null || variantCount <= 0) return null
  return totalElapsedSec / variantCount
}

/**
 * 標題橫幅的「耗時」在求解中要跟著進度事件動（issue #54），但 `SolverProgress.elapsedSec`
 * 是**本份變體**的耗時，一換下一份就歸零；`SolverJob.elapsedSec`（GET 拿到的）才是整個
 * 工作的耗時，可是 SSE 不會重推整個 job。這裡用累加器：每次事件到達時，若 `variantIndex`
 * 跟上一次不同，代表上一份變體結束了，把它最後看到的耗時併入基底再歸零計數，
 * 同一份變體內就直接取代顯示值。純函式、不碰時鐘，好測。
 */
export interface ElapsedAccumulator {
  /** 目前這份變體之前，所有已結束變體的耗時總和。 */
  readonly baseSec: number
  readonly variantIndex: number
  /** 目前這份變體最後一次事件回報的耗時。 */
  readonly lastVariantElapsedSec: number
}

/**
 * 接上一個還在跑的工作時建立累加器：`jobElapsedSec`（GET 當下的整體耗時，已含目前這份
 * 的部分進度）扣掉目前這份的耗時，回推出「之前幾份的耗時總和」當基底。
 */
export function initialElapsedAccumulator(
  jobElapsedSec: number,
  variantIndex: number,
  variantElapsedSec: number,
): ElapsedAccumulator {
  return { baseSec: Math.max(0, jobElapsedSec - variantElapsedSec), variantIndex, lastVariantElapsedSec: variantElapsedSec }
}

/**
 * 換下一份變體（`variantIndex` 改變）算一段結束，把上一段併入基底。另外，後端多樣性
 * 重試會對**同一個** `variantIndex` 再呼叫一次 `BeginVariant`，這時 `elapsedSec` 會
 * 從新的 0 開始算，同一份變體內的耗時看起來會「倒退」——這也要視為一段結束，
 * 不然畫面的總耗時會跟著退回去（協調者審查回饋）。
 */
export function accumulateElapsed(
  acc: ElapsedAccumulator,
  event: { variantIndex: number; elapsedSec: number },
): ElapsedAccumulator {
  const isNewSegment =
    acc.variantIndex > 0 && (event.variantIndex !== acc.variantIndex || event.elapsedSec < acc.lastVariantElapsedSec)
  if (isNewSegment) {
    return { baseSec: acc.baseSec + acc.lastVariantElapsedSec, variantIndex: event.variantIndex, lastVariantElapsedSec: event.elapsedSec }
  }
  return { baseSec: acc.baseSec, variantIndex: event.variantIndex, lastVariantElapsedSec: event.elapsedSec }
}

export function totalElapsedSec(acc: ElapsedAccumulator): number {
  return acc.baseSec + acc.lastVariantElapsedSec
}
