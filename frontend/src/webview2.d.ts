/**
 * WebView2 宿主注入的物件。只在正式版（WPF 殼內）存在，開發期為 undefined。
 *
 * 這些型別只應該被 src/realtime.ts 使用 —— 全專案唯一允許碰平台分支的檔案。
 * 見 docs/ARCHITECTURE.md §4.4。
 */
interface WebView2Host {
  addEventListener(
    type: 'message',
    listener: (event: { data: unknown }) => void,
  ): void
  removeEventListener(
    type: 'message',
    listener: (event: { data: unknown }) => void,
  ): void
  postMessage(message: unknown): void
}

interface Window {
  chrome?: {
    webview?: WebView2Host
  }
}
