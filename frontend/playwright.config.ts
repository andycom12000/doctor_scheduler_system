/**
 * 畫面層 E2E（issue #84）。不要直接 `npx playwright test`：伺服器（全新資料庫的後端 + vite）
 * 由 `npm run e2e`（scripts/e2e.ts）起好、收掉，並把 base URL 放進 E2E_BASE_URL。
 */
import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: 'e2e',
  // 單一後端、單一資料庫、劇本互相接續，不能平行
  workers: 1,
  fullyParallel: false,
  // 不重試：「連跑 3 次一致」要誠實，不讓 retry 遮掉 flake
  retries: 0,
  // 整條主線 3 分鐘內（求解約 20 秒，其餘是畫面操作與 API 建名冊）
  timeout: 180_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  outputDir: 'test-results',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5280',
    // 預設用 Playwright 自己的 Chromium（`npx playwright install chromium`）；
    // 想省下載、機器上有 Edge 時：E2E_BROWSER_CHANNEL=msedge npm run e2e
    channel: process.env.E2E_BROWSER_CHANNEL || undefined,
    viewport: { width: 1600, height: 1000 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
})
