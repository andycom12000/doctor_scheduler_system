/**
 * SCREEN 02（區域與點數）的純函式：可值類型顯示字串、點數欄位的顯示文字與輸入驗證、
 * 淺層 JSON 相等（判斷本地草稿是否偏離已載入的資料，決定儲存鈕是否亮起）。
 *
 * 不掛 DOM，全部進 vitest。
 */
import type { Area, AreaType, ConstraintSettings, EligibilityMatrix, MonthlyOverride, Rank } from '@/api/types'

/**
 * 某個身分依資格矩陣可值的區域類型名稱，依 `areaTypes` 給定的順序join（矩陣本身是
 * 無序的 key-value）。查不到或全部是 false 回「—」。
 */
export function eligibleAreaTypeNames(
  matrix: EligibilityMatrix['matrix'],
  rankCode: string,
  areaTypes: AreaType[],
): string {
  const row = matrix[rankCode] ?? {}
  const names = areaTypes.filter((type) => row[type.code]).map((type) => type.name)
  return names.length > 0 ? names.join('、') : '—'
}

/** `Rank.quotaCap`：`null` 只有 NP，顯示「不計」。 */
export function quotaCapDisplay(quotaCap: Rank['quotaCap']): string {
  return quotaCap === null ? '不計' : String(quotaCap)
}

/** `Rank.pointType`：`null` 只有 NP，顯示「不計」。 */
export function pointTypeDisplay(pointType: Rank['pointType']): string {
  return pointType === null ? '不計' : `Type ${pointType}`
}

/** 某個區域類型底下的區域，依 `areas` 給定的順序。 */
export function areasOfType(areaTypeCode: string, areas: Area[]): Area[] {
  return areas.filter((area) => area.areaTypeCode === areaTypeCode)
}

/**
 * 區域類型列右側的「每日 X 人 · 每區 Y 人」註記：X 是該類型底下所有區域
 * `requiredPerDay` 的加總，Y 取單一區域的 `requiredPerDay`。目前 5 個區域皆為 1
 * （`api-contract.yaml` 的說明），但欄位允許之後不同；若同一類型底下的區域
 * `requiredPerDay` 不一致，不硬湊一個「每區」數字，只顯示「每日」。
 */
export function areaTypeCapacityNote(areaTypeCode: string, areas: Area[]): string {
  const inType = areasOfType(areaTypeCode, areas)
  if (inType.length === 0) return ''
  const total = inType.reduce((sum, area) => sum + area.requiredPerDay, 0)
  const perAreaValues = new Set(inType.map((area) => area.requiredPerDay))
  if (perAreaValues.size === 1) return `每日 ${total} 人 · 每區 ${inType[0].requiredPerDay} 人`
  return `每日 ${total} 人`
}

export interface S7Status {
  label: string
  enabled: boolean
}

/**
 * 公平性點數段落的「S7 權重 X · 啟用／停用」標籤，狀態即時讀 `settings/constraints`
 * 的 `S7_FAIRNESS_POINT`（不是「預設」——使用者改過權重後這裡也會跟著變）。
 * 找不到那條軟約束（設定還沒載入）回 `null`。
 */
export function s7Status(constraints: ConstraintSettings | null): S7Status | null {
  const s7 = constraints?.soft.find((soft) => soft.code === 'S7_FAIRNESS_POINT')
  if (!s7) return null
  const enabled = s7.weight > 0
  return { label: `S7 權重 ${s7.weight} · ${enabled ? '啟用' : '停用'}`, enabled }
}

/**
 * 點數／額度欄位的輸入驗證：非負整數。空字串或 `v-model.number` 解析失敗時
 * Vue 會回退成原始字串，這裡一併擋掉，不讓 `''` 混進 PUT 的本體。
 */
export function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && Number.isInteger(value) && value >= 0
}

/** 天數視窗：至少 1 天的非負整數（0 天的視窗沒有意義）。 */
export function isPositiveInteger(value: unknown): value is number {
  return isNonNegativeInteger(value) && value >= 1
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
 * 本月覆寫的「有效形狀」正規化，比較前用。真後端 `GET` 一律回
 * `{ yearMonth, quotaCapByRank: {} }`（`JsonIgnoreCondition.Never`），MSW mock 未覆寫時
 * 省略整個 `quotaCapByRank`（回 `{ yearMonth }`）——語意相同（沒有任何身分被覆寫），
 * 但直接 JSON 比對會判定不相等。清空 R6 覆寫後草稿也可能是任一種形狀（見
 * `r6Override` 的 setter），一律正規化成「缺 key 當空物件」再比，兩邊後端都不會
 * 卡在「儲存鈕永遠亮著」。
 */
export function normalizeOverride(
  override: MonthlyOverride | null,
): { yearMonth: string; quotaCapByRank: Record<string, number> } | null {
  if (!override) return null
  return { yearMonth: override.yearMonth, quotaCapByRank: override.quotaCapByRank ?? {} }
}

/**
 * `settings/monthly-overrides/{ym}` 的 `useResource` key。讀取（`useResource` 的
 * `keyRef`）與寫入後的 `invalidate` 要組出同一個字串，抽成函式避免兩處字面量漂移。
 *
 * 年月位移（`shiftYearMonth`）與候選清單（`yearMonthOptions`）跟 `YearMonthSwitcher.vue`
 * 共用，定義在 `@/composables/useYearMonth`，不是這個檔案的職責（見審查意見 #48 S2）。
 */
export function monthlyOverrideKey(ym: string): string {
  return `settings/monthly-overrides/${ym}`
}
