import type { ScheduleStatus } from '@/api/types'

/**
 * 狀態 badge 的文字。發布後又改過（`editedSincePublish`，#75）時，狀態仍是已發布、版本號不變，
 * 但內容已經不是那一版，要另外標出來。
 */
export function scheduleStatusLabel(status: ScheduleStatus, publishedVersion: number | undefined, edited: boolean): string {
  if (status !== 'published') return '草稿'
  const base = publishedVersion ? `已發布 v${publishedVersion}` : '已發布'
  return edited ? `${base} · 有未發布的修改` : base
}
