/**
 * 純函式：某個 `YYYY-MM` 的全部日期字串。純日曆算術（哪個月有幾天），
 * 不涉及假日判斷——假日／國定假日一律要問 `useCalendar`，這裡只回天數。
 */
export function daysInMonth(ym: string): string[] {
  const [yearStr, monthStr] = ym.split('-')
  const year = Number(yearStr)
  const month = Number(monthStr)
  const lastDay = new Date(year, month, 0).getDate()
  return Array.from({ length: lastDay }, (_, i) => `${ym}-${String(i + 1).padStart(2, '0')}`)
}
