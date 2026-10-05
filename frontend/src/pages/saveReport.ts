/**
 * 設定頁（SCREEN 02／03）一次儲存會 PUT 好幾份文件（`Promise.allSettled`），
 * 這裡把每份的結果整理成一段給使用者看的文字：哪幾份存成功、哪幾份失敗與各自的原因。
 * 純函式，不碰 DOM。
 */

export interface SaveOutcome {
  /** 文件的人話名稱，例如「資格矩陣」。 */
  label: string
  result: PromiseSettledResult<unknown>
}

/**
 * 全部成功回 `null`。有失敗時每份失敗各佔一行（`「名稱」儲存失敗：原因`）；
 * 同時有成功的，最前面多一行「已儲存：…」，讓使用者知道不必重填那幾份。
 */
export function describeSaveFailures(outcomes: SaveOutcome[], describe: (err: unknown) => string): string | null {
  const failed = outcomes.filter((o): o is SaveOutcome & { result: PromiseRejectedResult } => o.result.status === 'rejected')
  if (failed.length === 0) return null

  const saved = outcomes.filter((o) => o.result.status === 'fulfilled').map((o) => o.label)
  const lines = failed.map((o) => `「${o.label}」儲存失敗：${describe(o.result.reason)}`)
  if (saved.length > 0) lines.unshift(`已儲存：${saved.join('、')}`)
  return lines.join('\n')
}
