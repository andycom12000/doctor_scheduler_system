/**
 * 草稿狀態下要「輸出」（匯出 Excel、列印）之前的共用提示（issue #34；#32 的列印接線也走這支）。
 * 三個出口：先發布再輸出／直接輸出草稿／取消。
 */
import { useConfirm } from '@/composables/useConfirm'

export type DraftOutputDecision = 'publish-first' | 'direct' | 'cancel'

/** `label` 是動作名稱（「匯出」「列印」），拼進文案與按鈕。 */
export async function promptDraftOutput(label: string): Promise<DraftOutputDecision> {
  const choice = await useConfirm().choose({
    title: '尚未發布',
    message: `這個月份的值班表還是草稿。要先發布再${label}嗎？`,
    confirmText: `先發布再${label}`,
    alternateText: `直接${label}草稿`,
    cancelText: '取消',
  })
  if (choice === 'confirm') return 'publish-first'
  if (choice === 'alternate') return 'direct'
  return 'cancel'
}

/** 上一次下載的 object URL；下一次下載時才收回（見 `downloadBlob`）。 */
let pendingUrl: string | null = null

/**
 * 把位元組交給瀏覽器下載：隱藏的 `<a download>` + object URL。
 *
 * URL 不能在計時器上收回：WebView2 殼會先跳「另存新檔」對話框，使用者選位置的這段時間
 * 下載還在讀這個 URL，提早收回會讓下載默默失敗（#33 實測，原本的 1 秒就不夠）。
 * 改成下一次下載時才收回上一個，同時只留一個，匯出檔又只有幾 KB。
 * 前提是同時只有一個下載在途：殼的存檔對話框是模態的，開著時整個視窗收不到輸入，
 * 使用者沒辦法再按一次匯出。對話框若改成非模態，這裡要改成每個下載各自收回。
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  if (pendingUrl) URL.revokeObjectURL(pendingUrl)
  const url = URL.createObjectURL(blob)
  pendingUrl = url
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
}
