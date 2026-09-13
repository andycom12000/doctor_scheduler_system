/**
 * SCREEN 05 的純函式邏輯：矩陣分組、底部兩列的可用人數、登記概況三個清單、
 * 可行性預警的逐日彙整與整月「R2/R3 優先 ICU 會被犧牲」提示、409 SOLVER_BUSY
 * 的 jobId 解析、寫入回應的就地套用。全部不碰 DOM／網路，方便 vitest 覆蓋
 * （issue #29 驗收：可行性提示的判斷是純函式）。
 */
import type {
  AreaType,
  BlockedDayEntry,
  BlockedDayMutationResult,
  BlockedDayRegistration,
  CalendarDay,
  EligibilityMatrix,
  FeasibilityReport,
  Rank,
  RankGroup,
  Staff,
} from '@/api/types'

// ---------------------------------------------------------------------------
// 矩陣分組（列＝在職人員依身分組分區）
// ---------------------------------------------------------------------------

export interface BlockedDayCellView {
  date: string
  blocked: boolean
  isHoliday: boolean
}

export interface BlockedDayRowView {
  staffId: string
  name: string
  rankCode: string
  employeeNo: string
  groupCode: string
  cells: BlockedDayCellView[]
  count: number
  remaining: number
  overCap: boolean
}

export interface BlockedDayGroupView {
  groupCode: string
  groupName: string
  capNote: string
  rows: BlockedDayRowView[]
  /**
   * 這個組在 `RankSettings.groups` 原始陣列裡的位置（過濾掉空組**之前**）。
   * 身分組色階（`--group-1`…`--group-4`）要對齊這個原始位置，不能用畫面上過濾空組後
   * 的 `v-for` 索引——不然空組被拿掉後，後面的組會集體往前跳一階顏色。
   */
  groupIndex: number
}

/** 由資格矩陣推導某個身分組「可值哪些區域類型」與「額度是否不計」的說明文字，不寫死區域類型代碼。 */
export function groupCapNote(params: {
  groupCode: string
  ranks: Rank[]
  matrix: EligibilityMatrix['matrix']
  areaTypeNameByCode: Record<string, string>
}): string {
  const ranksInGroup = params.ranks.filter((r) => r.groupCode === params.groupCode)
  const codes = new Set<string>()
  for (const rank of ranksInGroup) {
    const row = params.matrix[rank.code] ?? {}
    for (const [code, eligible] of Object.entries(row)) {
      if (eligible) codes.add(code)
    }
  }
  const names = [...codes].map((code) => params.areaTypeNameByCode[code] ?? code)
  const base = names.length > 0 ? `可值：${names.join('、')}` : '可值：（無資格）'
  const quotaless = ranksInGroup.length > 0 && ranksInGroup.every((r) => r.quotaCap === null)
  return quotaless ? `${base}（額度不計）` : base
}

/** 矩陣列，依 `groups` 給的順序分區；空組（目前沒有在職人員）不顯示。 */
export function buildGroupViews(params: {
  staff: Staff[]
  ranks: Rank[]
  groups: RankGroup[]
  byStaff: BlockedDayRegistration['byStaff']
  entries: BlockedDayEntry[]
  days: CalendarDay[]
  monthlyCap: number
  eligibilityMatrix: EligibilityMatrix['matrix']
  areaTypeNameByCode: Record<string, string>
}): BlockedDayGroupView[] {
  const rankByCode = new Map(params.ranks.map((r) => [r.code, r]))
  const totalsByStaff = new Map(params.byStaff.map((s) => [s.staffId, s]))

  const blockedByStaff = new Map<string, Set<string>>()
  for (const entry of params.entries) {
    const set = blockedByStaff.get(entry.staffId) ?? new Set<string>()
    set.add(entry.date)
    blockedByStaff.set(entry.staffId, set)
  }

  const rowsByGroup = new Map<string, BlockedDayRowView[]>()
  for (const person of params.staff) {
    if (person.status !== 'active') continue
    const rank = rankByCode.get(person.rankCode)
    const groupCode = rank?.groupCode ?? 'UNKNOWN'
    const totals = totalsByStaff.get(person.id)
    const blockedDates = blockedByStaff.get(person.id) ?? new Set<string>()
    const count = totals?.count ?? 0
    const remaining = totals?.remaining ?? Math.max(0, params.monthlyCap - count)
    const row: BlockedDayRowView = {
      staffId: person.id,
      name: person.name,
      rankCode: person.rankCode,
      employeeNo: person.employeeNo,
      groupCode,
      cells: params.days.map((day) => ({
        date: day.date,
        blocked: blockedDates.has(day.date),
        isHoliday: day.isHoliday,
      })),
      count,
      remaining,
      // 後端在 `count >= cap` 時 PUT 就回 409，不可能真的寫進一筆「超過上限」的登記，
      // `count > monthlyCap` 這種本地重算在真實資料下永遠是 false。真正該標紅的是
      // 「已經打平上限、沒有餘額」，也就是 remaining === 0。
      overCap: remaining === 0,
    }
    const list = rowsByGroup.get(groupCode) ?? []
    list.push(row)
    rowsByGroup.set(groupCode, list)
  }

  return params.groups
    .map((group, groupIndex) => ({
      groupCode: group.code,
      groupName: group.name,
      capNote: groupCapNote({
        groupCode: group.code,
        ranks: params.ranks,
        matrix: params.eligibilityMatrix,
        areaTypeNameByCode: params.areaTypeNameByCode,
      }),
      rows: rowsByGroup.get(group.code) ?? [],
      groupIndex,
    }))
    .filter((group) => group.rows.length > 0)
}

// ---------------------------------------------------------------------------
// 底部兩列：該日登記人數、總值可用人數
// ---------------------------------------------------------------------------

/** `byDate` 只列有登記的日期；補上整月每一天，沒登記的日子算 0。 */
export function dateCountMap(byDate: BlockedDayRegistration['byDate'], days: string[]): Map<string, number> {
  const map = new Map(days.map((date) => [date, 0]))
  for (const entry of byDate) {
    if (map.has(entry.date)) map.set(entry.date, entry.count)
  }
  return map
}

/**
 * 找出「總值」這個區域類型的代碼，不比對顯示名稱字串（`name === '總值'` 太脆弱，改名字就找不到）。
 * 優先順序：
 * 1. `feasibility.bySupply[0].areaTypeCodes[0]`——巢狀累計供需的最窄那層，定義上就是資格
 *    最少人能值的區域類型（ARCHITECTURE §9.1：總值 ⊂ 資深 ⊂ …）。
 * 2. 沒有可行性資料時，從資格矩陣挑「可值人數最少」的區域類型。
 * 3. 兩者都沒有資料（例如設定尚未載入）時，回退契約既有的 `CHIEF` 代碼。
 */
export function chiefAreaTypeCode(params: {
  bySupply: FeasibilityReport['bySupply']
  matrix: EligibilityMatrix['matrix']
  areaTypes: AreaType[]
}): string {
  const narrowestLayer = params.bySupply[0]?.areaTypeCodes
  if (narrowestLayer?.length === 1) return narrowestLayer[0]

  // 資格矩陣還沒載入時是 `{}`，每個區域類型的「可值人數」都會算成 0，
  // 排最前面的那個（例如 WARD）會被誤判成「可值人數最少」；矩陣是空的就直接跳過這條路徑，
  // 交給最後的 'CHIEF' fallback，不要用一個沒意義的 0 打平去猜。
  if (Object.keys(params.matrix).length === 0) return 'CHIEF'

  let bestCode: string | null = null
  let bestEligibleCount = Number.POSITIVE_INFINITY
  for (const type of params.areaTypes) {
    const eligibleCount = Object.values(params.matrix).filter((row) => row[type.code] === true).length
    if (eligibleCount < bestEligibleCount) {
      bestEligibleCount = eligibleCount
      bestCode = type.code
    }
  }
  return bestCode ?? 'CHIEF'
}

/** 每一天「有資格值總值且未登記」的在職人數；資格由資格矩陣依 `rankCode` 推出。 */
export function chiefAvailabilityByDate(params: {
  staff: Staff[]
  matrix: EligibilityMatrix['matrix']
  chiefCode: string | null
  entries: BlockedDayEntry[]
  days: string[]
}): Map<string, number> {
  const result = new Map(params.days.map((date) => [date, 0]))
  if (!params.chiefCode) return result
  const chiefCode = params.chiefCode

  const eligibleIds = new Set(
    params.staff
      .filter((s) => s.status === 'active' && params.matrix[s.rankCode]?.[chiefCode] === true)
      .map((s) => s.id),
  )

  const blockedCountByDate = new Map(params.days.map((date) => [date, 0]))
  for (const entry of params.entries) {
    if (!eligibleIds.has(entry.staffId)) continue
    if (!blockedCountByDate.has(entry.date)) continue
    blockedCountByDate.set(entry.date, (blockedCountByDate.get(entry.date) ?? 0) + 1)
  }

  for (const date of params.days) {
    result.set(date, eligibleIds.size - (blockedCountByDate.get(date) ?? 0))
  }
  return result
}

// ---------------------------------------------------------------------------
// 登記概況：總筆數、已達上限、尚未登記
// ---------------------------------------------------------------------------

export interface StaffCountView {
  staffId: string
  name: string
  count: number
}

/**
 * 已達上限（`remaining === 0`）的人。後端在 `count >= cap` 時 PUT 就回 409，
 * 所以「超過上限」在真實資料下永遠不會發生，能列出來的只有「剛好打平、沒有餘額」。
 */
export function overCapEntries(byStaff: BlockedDayRegistration['byStaff'], nameById: Map<string, string>): StaffCountView[] {
  return byStaff
    .filter((s) => s.remaining === 0)
    .map((s) => ({ staffId: s.staffId, name: nameById.get(s.staffId) ?? s.staffId, count: s.count }))
}

export function unregisteredEntries(
  byStaff: BlockedDayRegistration['byStaff'],
  nameById: Map<string, string>,
): StaffCountView[] {
  return byStaff
    .filter((s) => s.count === 0)
    .map((s) => ({ staffId: s.staffId, name: nameById.get(s.staffId) ?? s.staffId, count: s.count }))
}

export function totalRegisteredCount(byStaff: BlockedDayRegistration['byStaff']): number {
  return byStaff.reduce((sum, s) => sum + s.count, 0)
}

// ---------------------------------------------------------------------------
// 可行性預警 · 逐日
// ---------------------------------------------------------------------------

export interface ShortageDayView {
  date: string
  hasChiefShortage: boolean
  text: string
}

/** 只列出實際有缺口的日子（`shortages` 非空）；總值缺口排最前面，其餘依日期遞增。 */
export function buildShortageDays(
  byDate: FeasibilityReport['byDate'],
  chiefCode: string | null,
  areaTypeNameByCode: Record<string, string>,
): ShortageDayView[] {
  return byDate
    .filter((d) => d.shortages.length > 0)
    .map((d) => {
      const shortages = [...d.shortages].sort((a, b) => {
        const aIsChief = a.areaTypeCode === chiefCode
        const bIsChief = b.areaTypeCode === chiefCode
        if (aIsChief === bIsChief) return 0
        return aIsChief ? -1 : 1
      })
      const hasChiefShortage = shortages.some((s) => s.areaTypeCode === chiefCode)
      const text = shortages
        .map((s) => {
          const name = areaTypeNameByCode[s.areaTypeCode ?? ''] ?? s.areaTypeCode ?? '未知區域類型'
          return `${name}不足（可用 ${s.availableStaff ?? '—'}／需 ${s.required ?? '—'}）`
        })
        .join('、')
      return { date: d.date, hasChiefShortage, text }
    })
    .sort((a, b) => {
      if (a.hasChiefShortage !== b.hasChiefShortage) return a.hasChiefShortage ? -1 : 1
      return a.date.localeCompare(b.date)
    })
}

// ---------------------------------------------------------------------------
// 可行性預警 · 整月供需（R2/R3 優先 ICU 會被犧牲）
// ---------------------------------------------------------------------------

/**
 * 「一般病房需求 vs 低年級剩餘供給」吃緊的門檻。這裡的邊際供給／需求是從 `bySupply` 巢狀
 * 累計的最後兩層相減得出（見下方函式註解），對應 ARCHITECTURE §9.1 零登記基準：低年級
 * 專屬供給 110 點、一般病房專屬需求 117 點，110/117 ≈ 0.94——連基準都打不平；
 * 沿用設計稿的 1.5 倍門檻校準。
 */
const WARD_SQUEEZE_RATIO_THRESHOLD = 1.5

export interface WardSqueezeHint {
  /** 是否顯示「R2/R3 優先 ICU 會被犧牲」提示。 */
  show: boolean
  /** 邊際供給（低年級可用供給）與邊際需求（一般病房需求）的比值；`marginalDemand <= 0` 時為 null。 */
  ratio: number | null
  marginalHeadroom: number
  marginalDemand: number
}

/**
 * 只在 `bySupply` 恰好三層（總值 ⊂ 資深；總值＋ICU ⊂ 資深＋中階；全部 ⊂ 全部）時才成立——
 * 資格矩陣若不是巢狀的，這三道不等式就不對應真實的 Hall 條件，回 `null` 表示不適用，
 * 呼叫端不顯示提示（見 issue #29、ARCHITECTURE §9.1）。
 *
 * 最後一層（全部）與倒數第二層（總值＋ICU）的 `headroom` 差，等於「一般病房」這個邊際層
 * 自己的供需缺口：headroom 定義為 supply − demand，兩層相減時資深＋中階的部分互相抵消，
 * 只剩下低年級（一般病房專屬）的供給與一般病房專屬需求，不需要另外重新統計人數。
 */
export function evaluateWardSqueezeHint(bySupply: FeasibilityReport['bySupply']): WardSqueezeHint | null {
  if (bySupply.length !== 3) return null
  const midLayer = bySupply[1]
  const fullLayer = bySupply[2]
  const marginalHeadroom = fullLayer.headroom - midLayer.headroom
  const marginalDemand = fullLayer.demandPoints - midLayer.demandPoints

  if (marginalDemand <= 0) {
    return { show: false, ratio: null, marginalHeadroom, marginalDemand }
  }

  const marginalSupply = marginalHeadroom + marginalDemand
  const ratio = marginalSupply / marginalDemand
  return { show: ratio < WARD_SQUEEZE_RATIO_THRESHOLD, ratio, marginalHeadroom, marginalDemand }
}

// ---------------------------------------------------------------------------
// 可行性預警 · 整月供需的「倍率」＋進度條（issue #53：改成設計稿的 9.1× 樣式，
// 「餘裕 N」降級為副文字，不再是主要視覺）
// ---------------------------------------------------------------------------

export interface SupplyRatioView {
  /** `supplyPoints / demandPoints`；`demandPoints` 為 0 時無意義，回 null。 */
  ratio: number | null
  /** 「9.1×」這種顯示字串；`ratio` 為 null 時是 `—`。 */
  ratioLabel: string
  /**
   * 進度條寬度（0–100）：`demandPoints / supplyPoints`。`supplyPoints` ≤ 0 時，
   * 只要還有需求（`demandPoints > 0`）就是滿條 100（供給掛零、需求全部落空，
   * 是最嚴重的情況，不能顯示成空條）；`demandPoints` 也 ≤ 0（沒有需求也沒有供給）才是 0。
   */
  utilizationPercent: number
  /** `supply − demand`，原樣帶出給副文字用（不重算）。 */
  headroom: number
}

/**
 * 額度點數（`FeasibilityReport.bySupply` 的 `demandPoints`／`supplyPoints`，見
 * api-contract.yaml：`supplyPoints` 由「額度上限」與「未登記日的額度點數」推得）供需的
 * 倍率與進度條。倍率是「供給是需求的幾倍」，進度條是反過來的「需求吃掉多少供給」，
 * 兩個方向不一樣、不要互相除導出。
 */
export function computeSupplyRatio(layer: {
  demandPoints: number
  supplyPoints: number
  headroom: number
}): SupplyRatioView {
  const { demandPoints, supplyPoints, headroom } = layer
  const ratio = demandPoints > 0 ? supplyPoints / demandPoints : null
  const ratioLabel = ratio === null ? '—' : `${ratio.toFixed(1)}×`
  const utilizationPercent =
    supplyPoints > 0 ? Math.min(100, Math.round((demandPoints / supplyPoints) * 100)) : demandPoints > 0 ? 100 : 0
  return { ratio, ratioLabel, utilizationPercent, headroom }
}

// ---------------------------------------------------------------------------
// 帶入求解：409 SOLVER_BUSY 解析正在跑的 jobId
// ---------------------------------------------------------------------------

export function extractBusyJobId(body: unknown): string | null {
  if (!body || typeof body !== 'object' || !('error' in body)) return null
  const error = (body as { error?: unknown }).error
  if (!error || typeof error !== 'object' || !('details' in error)) return null
  const details = (error as { details?: unknown }).details
  if (!details || typeof details !== 'object') return null
  const jobId = (details as { jobId?: unknown }).jobId
  return typeof jobId === 'string' ? jobId : null
}

// ---------------------------------------------------------------------------
// 寫入回應就地套用：不整份重抓 GET /blocked-days/{ym}
// ---------------------------------------------------------------------------

function upsertByDate(byDate: BlockedDayRegistration['byDate'], date: string, count: number): void {
  const index = byDate.findIndex((d) => d.date === date)
  if (count <= 0) {
    if (index >= 0) byDate.splice(index, 1)
    return
  }
  if (index >= 0) {
    byDate[index] = { date, count }
    return
  }
  byDate.push({ date, count })
  byDate.sort((a, b) => a.date.localeCompare(b.date))
}

/** 直接改寫 `registration`（`entries`／`byStaff`／`byDate`），呼叫端不需要重新 GET 整份登記表。 */
export function applyBlockedDayMutation(
  registration: BlockedDayRegistration,
  staffId: string,
  date: string,
  blocked: boolean,
  result: BlockedDayMutationResult,
): void {
  const entryIndex = registration.entries.findIndex((e) => e.staffId === staffId && e.date === date)
  if (blocked && entryIndex < 0) {
    registration.entries.push({ staffId, date })
  } else if (!blocked && entryIndex >= 0) {
    registration.entries.splice(entryIndex, 1)
  }

  const staffRow = registration.byStaff.find((s) => s.staffId === staffId)
  if (staffRow) {
    if (typeof result.staffTotals?.count === 'number') staffRow.count = result.staffTotals.count
    if (typeof result.staffTotals?.remaining === 'number') staffRow.remaining = result.staffTotals.remaining
  }

  if (typeof result.dateTotals?.count === 'number') {
    upsertByDate(registration.byDate, date, result.dateTotals.count)
  }
}
