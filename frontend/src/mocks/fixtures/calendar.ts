/**
 * 行事曆的事實來源（週六日 + 國定假日 + 補班日）。
 *
 * **近似值聲明**：本環境無法連網查證，2026 年的國定假日與補班日是依台灣曆法慣例
 * （農曆春節、228、清明兒童節合併假、勞動節、端午、中秋、國慶）推算的合理近似，
 * 不保證與行政院人事總處公告逐日相符。排班者可用 `PATCH /calendars/{year}/{date}`
 * 逐日覆寫，這也是這支端點存在的理由。其餘年份沒有國定假日資料，
 * 只依週六日判斷 `isHoliday`——這同樣是刻意的近似，不是遺漏。
 *
 * `quotaPointValue` 不在這裡算——它相依於 `PointRules`，由 `domain.ts` 在讀取時
 * 用「這裡的事實」乘查表算出，兩套點數與行事曆事實才不會耦合在一起。
 */

export interface CalendarDayFacts {
  date: string
  /** 0 = 週日 */
  weekday: number
  isPublicHoliday: boolean
  isMakeUpWorkday: boolean
  holidayName: string | null
}

/** 2026 年國定假日（近似，見檔頭聲明）。不含一般週六、週日。 */
const publicHolidays2026: Record<string, string> = {
  '2026-01-01': '元旦',
  '2026-02-16': '農曆除夕',
  '2026-02-17': '春節',
  '2026-02-18': '春節',
  '2026-02-19': '春節',
  '2026-02-20': '春節',
  '2026-02-28': '和平紀念日',
  '2026-04-04': '兒童節（合併清明節）',
  '2026-05-01': '勞動節',
  '2026-06-19': '端午節',
  '2026-09-25': '中秋節',
  '2026-10-10': '國慶日',
}

/** 因春節連假調移而上班的週六（近似）。視為平日。 */
const makeUpWorkdays2026: Set<string> = new Set(['2026-02-14'])

function pad2(n: number): string {
  return String(n).padStart(2, '0')
}

function toDateStr(year: number, month1: number, day: number): string {
  return `${year}-${pad2(month1)}-${pad2(day)}`
}

function daysInMonth(year: number, month1: number): number {
  return new Date(Date.UTC(year, month1, 0)).getUTCDate()
}

function weekdayOf(dateStr: string): number {
  return new Date(`${dateStr}T00:00:00Z`).getUTCDay()
}

/** 單日的行事曆事實。目前只有 2026 年有國定假日／補班日資料，其餘年份只判斷週六日。 */
export function calendarDayFacts(dateStr: string): CalendarDayFacts {
  const year = dateStr.slice(0, 4)
  const isPublicHoliday = year === '2026' ? Object.hasOwn(publicHolidays2026, dateStr) : false
  const isMakeUpWorkday = year === '2026' ? makeUpWorkdays2026.has(dateStr) : false
  const holidayName = (year === '2026' ? publicHolidays2026[dateStr] : undefined) ?? null
  return {
    date: dateStr,
    weekday: weekdayOf(dateStr),
    isPublicHoliday,
    isMakeUpWorkday,
    holidayName,
  }
}

/** 整年度逐日事實，`GET /calendars/{year}` 的資料本體。 */
export function calendarYearFacts(year: number): CalendarDayFacts[] {
  const out: CalendarDayFacts[] = []
  for (let month1 = 1; month1 <= 12; month1++) {
    const count = daysInMonth(year, month1)
    for (let day = 1; day <= count; day++) {
      out.push(calendarDayFacts(toDateStr(year, month1, day)))
    }
  }
  return out
}

/** 基準假日判定（未套用使用者覆寫）：週六、週日或國定假日，但補班日視為平日。 */
export function baseIsHoliday(facts: CalendarDayFacts): boolean {
  if (facts.isMakeUpWorkday) return false
  return facts.weekday === 0 || facts.weekday === 6 || facts.isPublicHoliday
}

export function daysInYearMonth(ym: string): number {
  const [year, month] = ym.split('-').map(Number)
  return daysInMonth(year, month)
}

export function datesOfYearMonth(ym: string): string[] {
  const [year, month] = ym.split('-').map(Number)
  const count = daysInMonth(year, month)
  const out: string[] = []
  for (let day = 1; day <= count; day++) out.push(toDateStr(year, month, day))
  return out
}

/** `a − b` 的天數差（可正可負）。用於值休休間隔與連續天數檢查。 */
export function diffDays(a: string, b: string): number {
  const da = Date.parse(`${a}T00:00:00Z`)
  const db = Date.parse(`${b}T00:00:00Z`)
  return Math.round((da - db) / 86_400_000)
}

/** `date` 的隔一天（可能跨月／跨年，公平性點數表要看這個）。 */
export function nextDate(dateStr: string): string {
  const d = new Date(`${dateStr}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + 1)
  return d.toISOString().slice(0, 10)
}

/** `date` 的前一天（可能跨月／跨年，跨月尾巴查詢要看這個）。 */
export function previousDate(dateStr: string): string {
  const d = new Date(`${dateStr}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() - 1)
  return d.toISOString().slice(0, 10)
}

/** `ym` 的前一個年月，例：`2026-01` → `2025-12`。 */
export function previousYearMonth(ym: string): string {
  const [year, month] = ym.split('-').map(Number)
  return month === 1 ? `${year - 1}-12` : `${year}-${pad2(month - 1)}`
}

/** `ym` 的下一個年月，例：`2025-12` → `2026-01`。 */
export function nextYearMonth(ym: string): string {
  const [year, month] = ym.split('-').map(Number)
  return month === 12 ? `${year + 1}-01` : `${year}-${pad2(month + 1)}`
}
