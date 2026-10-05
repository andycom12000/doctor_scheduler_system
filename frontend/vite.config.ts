import { existsSync, readFileSync, rmSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import { join, isAbsolute, resolve } from 'node:path'
import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

/**
 * `public/mockServiceWorker.js` 只有 `dev:mock` 需要，不該原樣進 `wwwroot/`（issue #26）。
 * 用 `closeBundle` 而非把它從 `public/` 移走，避免連 `dev:mock` 都要另外指定 `publicDir`。
 */
function excludeMockServiceWorker(): Plugin {
  let outDir = ''
  return {
    name: 'exclude-mock-service-worker',
    apply: 'build',
    configResolved(config) {
      outDir = config.build.outDir
    },
    closeBundle() {
      const dir = isAbsolute(outDir) ? outDir : resolve(process.cwd(), outDir)
      const file = join(dir, 'mockServiceWorker.js')
      if (existsSync(file)) rmSync(file)
    },
  }
}

/**
 * 建置目標對齊隨附的 WebView2 Fixed Version runtime。
 *
 * 這是隨附 fixed-version 的免費好處：不需 polyfill 與 legacy transpile，bundle 更小。
 * 代價是必須在真正的 fixed-version runtime 內測試，不可只在最新版 Chrome 驗證。
 *
 * 提高這個數字前，先確認 build/webview2.json 裡實際隨附的 runtime 版本。
 * 見 docs/ARCHITECTURE.md §5。
 */
const WEBVIEW2_CHROMIUM_TARGET = 'chrome152' // build/webview2.json：152.0.4191.62

/**
 * 版本號唯一來源是 repo 根目錄 Directory.Build.props 的 <Version>。
 * 建置時注入成 `__APP_VERSION__`；開發伺服器（dev、dev:mock）加 `-dev` 後綴，與發佈包區分。
 */
function readAppVersion(): string {
  const props = readFileSync(
    fileURLToPath(new URL('../Directory.Build.props', import.meta.url)),
    'utf8',
  )
  // 先去掉 XML 註解（props 的說明文字裡就有「<Version>」字樣），publish.ps1 同樣處理
  const m = props.replace(/<!--[\s\S]*?-->/g, '').match(/<Version>\s*([^<\s]+)\s*<\/Version>/)
  if (!m) throw new Error('Directory.Build.props 找不到 <Version>')
  return m[1]
}

export default defineConfig(({ command }) => ({
  define: {
    __APP_VERSION__: JSON.stringify(
      command === 'serve' ? `${readAppVersion()}-dev` : readAppVersion(),
    ),
  },

  plugins: [vue(), excludeMockServiceWorker()],

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
}))
