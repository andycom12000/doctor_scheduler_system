/**
 * 以 VITE_USE_MOCK=true 啟動時改走 MSW，不需要跑 Scheduler.Api，
 * 也不需要安裝 .NET。見 frontend/README.md。
 */
export async function startMocksIfEnabled(): Promise<void> {
  if (import.meta.env.VITE_USE_MOCK !== 'true') return

  // 0 人情境（issue #81）：網址帶 ?mock=no-staff 啟動時名冊為空，用來驗證空名冊引導。
  if (new URLSearchParams(location.search).get('mock') === 'no-staff') {
    const { resetStore } = await import('./store')
    resetStore({ noStaff: true })
  }

  const { worker } = await import('./browser')
  await worker.start({
    // 契約尚未涵蓋的請求原樣放行，方便逐步補齊 handler。
    onUnhandledRequest: 'bypass',
  })
}
