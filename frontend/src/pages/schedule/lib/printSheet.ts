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

/**
 * Ctrl+P（含 Ctrl+Shift+P 的系統列印對話框）：排班主表攔下來改走「列印」按鈕的流程，
 * 才不會繞過同人同日兩區擋印與草稿提示。右鍵選單的列印由 Shell 拿掉（#32）。
 */
export function isPrintShortcut(e: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'altKey'>): boolean {
  return (e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'p'
}

/** 紙本標題。草稿也能印（草稿提示選「直接列印草稿」），所以狀態一定要印出來。 */
export function printHeading(ym: string, status: ScheduleStatus, publishedVersion: number): string {
  const month = `${Number(ym.slice(0, 4))} 年 ${Number(ym.slice(5, 7))} 月值班表`
  if (status !== 'published') return `${month}（草稿）`
  return publishedVersion ? `${month}（已發布 v${publishedVersion}）` : `${month}（已發布）`
}

/**
 * 圖例旁的假日清單（格子裡放不下名稱）：`10/09 國慶日補假、10/10 國慶日`。當月沒有回 null。
 * - `public`：國定假日，有沒有名稱都列（斜線底紋）。
 * - `other`：國定假日以外的假日，只列有名稱的（例如逐日覆寫的颱風假）；一般週末不列（灰底）。
 */
export function holidayNote(days: readonly DayColumn[], kind: 'public' | 'other'): string | null {
  const items = days
    .filter((d) => (kind === 'public' ? d.isPublicHoliday : d.isHoliday && !d.isPublicHoliday && d.holidayName))
    .map((d) => {
      const md = `${d.date.slice(5, 7)}/${d.dd}`
      return d.holidayName ? `${md} ${d.holidayName}` : md
    })
  return items.length > 0 ? items.join('、') : null
}
