/**
 * 列印（#32）的純函式。版面本身是 `@media print` 的 CSS（`styles.css` 與各格線元件），
 * 這裡只放「印哪個檢視」「紙上的標題」「國定假日註記」這幾個要釘住的決定。
 */
import type { ScheduleStatus } from '@/api/types'
import type { DayColumn } from './scheduleGrid'

export type ScheduleTab = 'area-by-day' | 'day-by-staff' | 'day-detail'
export type PrintTab = Exclude<ScheduleTab, 'day-detail'>

/** 兩種格線照目前檢視印（跟匯出的 `exportLayoutFor` 同一條規則）；單日詳表沒有列印版面，改印日 × 人。 */
export function printTabFor(tab: ScheduleTab): PrintTab {
  return tab === 'area-by-day' ? 'area-by-day' : 'day-by-staff'
}

/** 紙本標題。草稿也能印（草稿提示選「直接列印草稿」），所以狀態一定要印出來。 */
export function printHeading(ym: string, status: ScheduleStatus, publishedVersion: number): string {
  const month = `${Number(ym.slice(0, 4))} 年 ${Number(ym.slice(5, 7))} 月值班表`
  if (status !== 'published') return `${month}（草稿）`
  return publishedVersion ? `${month}（已發布 v${publishedVersion}）` : `${month}（已發布）`
}

/** 圖例旁的國定假日清單（格子裡放不下名稱）：`10/09 國慶日補假、10/10 國慶日`。當月沒有回 null。 */
export function publicHolidayNote(days: readonly DayColumn[]): string | null {
  const items = days
    .filter((d) => d.isPublicHoliday)
    .map((d) => {
      const md = `${d.date.slice(5, 7)}/${d.dd}`
      return d.holidayName ? `${md} ${d.holidayName}` : md
    })
  return items.length > 0 ? items.join('、') : null
}
