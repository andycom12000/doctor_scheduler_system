import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

/**
 * 建置目標對齊隨附的 WebView2 Fixed Version runtime。
 *
 * 這是隨附 fixed-version 的免費好處：不需 polyfill 與 legacy transpile，bundle 更小。
 * 代價是必須在真正的 fixed-version runtime 內測試，不可只在最新版 Chrome 驗證。
 *
 * 提高這個數字前，先確認 build/webview2.json 裡實際隨附的 runtime 版本。
 * 見 docs/ARCHITECTURE.md §5。
 */
const WEBVIEW2_CHROMIUM_TARGET = 'chrome120'

export default defineConfig({
  plugins: [vue()],

  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },

  build: {
    target: WEBVIEW2_CHROMIUM_TARGET,
    // 正式版由 WPF 殼在 WebResourceRequested 裡讀 wwwroot 回靜態檔（ARCHITECTURE §6.2），故直接輸出到殼的 wwwroot。
    outDir: '../src/Scheduler.Shell/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
  },

  server: {
    port: 5173,
    // 開發期：Vite dev server 以 proxy 轉送 /api 到 Scheduler.Api。
    // 正式版：同樣的 fetch('/api/...') 由 WebResourceRequested 攔截。
    // 前端程式碼兩者逐字相同 —— 這是遷移路徑每天都在被使用的原因。
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: false,
      },
    },
  },
})
