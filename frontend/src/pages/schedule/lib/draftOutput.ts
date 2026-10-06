/**
 * 草稿狀態下要「輸出」（匯出 Excel、列印）之前的共用提示（issue #34；#32 的列印接線也走這支）。
 * 三個出口：先發布再輸出／直接輸出草稿／取消。發布後又改過（#75）也走這支，改問要不要重新發布。
 */
import type { ScheduleStatus } from '@/api/types'
import { useConfirm } from '@/composables/useConfirm'

export type DraftOutputDecision = 'publish-first' | 'direct' | 'cancel'

/** 輸出前要不要問、問哪一種：草稿問先發布，發布後有修改問先重新發布，已發布沒改過不問。 */
export function outputPromptKind(status: ScheduleStatus, editedSincePublish: boolean): 'draft' | 'edited' | null {
  if (status !== 'published') return 'draft'
  return editedSincePublish ? 'edited' : null
}

/**
 * `label` 是動作名稱（「匯出」「列印」），拼進文案與按鈕。
 * 帶 `editedFrom`（最近發布的版本號）就是「發布後有修改」的版本。
 */
export async function promptDraftOutput(
  label: string,
  options: { editedFrom?: number } = {},
): Promise<DraftOutputDecision> {
  const { editedFrom } = options
  const choice = await useConfirm().choose(
    editedFrom === undefined
      ? {
          title: '尚未發布',
          message: `這個月份的值班表還是草稿。要先發布再${label}嗎？`,
          confirmText: `先發布再${label}`,
          alternateText: `直接${label}草稿`,
          cancelText: '取消',
        }
      : {
          title: '發布後有修改',
          message: `發布 v${editedFrom} 之後又改過，目前的內容還沒有重新發布。要先重新發布再${label}嗎？`,
          confirmText: `先重新發布再${label}`,
          alternateText: `直接${label}`,
          cancelText: '取消',
        },
  )
  if (choice === 'confirm') return 'publish-first'
  if (choice === 'alternate') return 'direct'
  return 'cancel'
}
