/**
 * SCREEN 03（資格與約束）的純函式：`ConstraintScope` → 顯示字串、`params` → 顯示字串／
 * 方向文字、權重輸入驗證。不掛 DOM，全部進 vitest。
 *
 * 詞彙依 CONTEXT.md：日類三種——平日、假日、國定假日，三者不可混用或合併顯示。
 */
import type { ConstraintScope, Metric } from '@/api/types'

const DAY_KIND_LABELS: Record<string, string> = {
  weekday: '平日',
  holiday: '假日',
  publicHoliday: '國定假日',
}

const METRIC_LABELS: Record<Metric, string> = {
  quota_point: '額度點數',
  fairness_point: '公平性點數',
  duty_day: '值班天數',
}

/**
 * 三維 scope（身分／區域類型／日類）→ 一行顯示字串。
 * 省略的維度代表「全部」，不特別列出；`exemptRankCodes` 標「除外」。
 * 三個維度都沒有時回「全體」。
 */
export function describeScope(scope: ConstraintScope | null | undefined): string {
  if (!scope) return '全體'

  const parts: string[] = []
  if (scope.rankCodes && scope.rankCodes.length > 0) {
    parts.push(scope.rankCodes.join('/'))
  }
  if (scope.exemptRankCodes && scope.exemptRankCodes.length > 0) {
    parts.push(`${scope.exemptRankCodes.join('/')} 除外`)
  }
  if (scope.areaTypeCodes && scope.areaTypeCodes.length > 0) {
    parts.push(scope.areaTypeCodes.join('/'))
  }
  if (scope.dayKinds && scope.dayKinds.length > 0) {
    parts.push(scope.dayKinds.map((kind) => DAY_KIND_LABELS[kind] ?? kind).join('/'))
  }

  return parts.length > 0 ? parts.join(' · ') : '全體'
}

/** `Budget`／`Fairness` 原語專用的度量顯示字串；其餘原語沒有 metric，回 null。 */
export function describeMetric(metric: Metric | null | undefined): string | null {
  if (!metric) return null
  return METRIC_LABELS[metric] ?? metric
}

/**
 * `params` → 顯示字串，`direction` 另由 `describeDirection` 處理、這裡略過，
 * 避免「避開」被印成看起來像數值的東西。查不到已知欄位時回「—」。
 */
export function describeParams(params: Record<string, unknown> | null | undefined): string {
  if (!params) return '—'

  const parts: string[] = []
  if (typeof params.days === 'number') parts.push(`${params.days} 天`)
  if (typeof params.cap === 'number') parts.push(`上限 ${params.cap}`)

  return parts.length > 0 ? parts.join(' · ') : '—'
}

/**
 * 軟約束的「方向」：`prefer` 顯示優先、`avoid` 顯示避開，兩者都不顯示負權重
 * （契約明文權重沒有負值）。沒有 direction 的軟約束（Fairness／Consistency）回 null。
 */
export function describeDirection(params: Record<string, unknown> | null | undefined): '優先' | '避開' | null {
  const direction = params?.direction
  if (direction === 'prefer') return '優先'
  if (direction === 'avoid') return '避開'
  return null
}

/** 軟約束權重：0–100 的整數，0 即停用。 */
export function isValidWeight(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && Number.isInteger(value) && value >= 0 && value <= 100
}

/**
 * 淺層 JSON 相等，用來判斷本地草稿是否偏離「已載入 / 已儲存」的版本。
 * 兩邊都是從同一份資料深拷貝出來的物件（或其中一份剛從後端重抓），
 * key 順序理論上一致，用 `JSON.stringify` 比對夠用，不需要真正的深比較。
 */
export function isEqualJson(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b)
}

/**
 * 深拷貝草稿用。`useResource` 的 `data` 是 `reactive()` proxy，`structuredClone`
 * 會因為 proxy 帶著額外的內部 slot 而丟 `DataCloneError`；用 JSON 往返繞開，
 * 反正這裡的資料本來就是純 JSON 形狀（API 回應），沒有 Date／Map 等需要保留的型別。
 */
export function cloneJson<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

/**
 * 硬約束的「參數欄」：`metric`（Budget／Fairness 原語）與 `params`（days／cap）可能同時存在
 * （例如 H6_NP_MONTHLY_DAYS 兩者都有），不能用 `??` 互相蓋掉，兩個都要顯示。
 */
export function describeHardConstraintParams(
  metric: Metric | null | undefined,
  params: Record<string, unknown> | null | undefined,
): string {
  const parts = [describeMetric(metric), describeParams(params)].filter(
    (part): part is string => part !== null && part !== '—',
  )
  return parts.length > 0 ? parts.join(' · ') : '—'
}
