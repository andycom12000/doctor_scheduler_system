import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'

/**
 * 獨立於 vite.config.ts：測試只測純函式與 composable，不需要 @vitejs/plugin-vue，
 * 也不需要跟 vue-tsc / build 共用設定（vitest 的 bundled vite 版本與 vite 8 的搭配
 * 用獨立設定比較不會互相牽扯）。
 */
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'node',
  },
})
