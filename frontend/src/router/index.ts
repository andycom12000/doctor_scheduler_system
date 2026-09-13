/**
 * history 模式：Shell 的 `WebResourceRequested` 對無副檔名路徑一律回 `index.html`
 * （ARCHITECTURE §6.2），子路徑重新整理不會 404。
 *
 * 路由表本體在 `routes.ts`（純資料，`routes.test.ts` 用 `createMemoryHistory` 驗證）。
 */
import { createRouter, createWebHistory } from 'vue-router'
import { routes } from './routes'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})
