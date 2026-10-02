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

/** 把位元組交給瀏覽器下載：隱藏的 `<a download>` + object URL，用完立刻 revoke。 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}
