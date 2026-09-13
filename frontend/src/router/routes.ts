/**
 * 六條路由全部指向 `src/pages/<screen>/index.vue` 的佔位頁，畫面 PR 只改頁面內容，
 * 不改這份路由表（frontend-plan.md §4 的規則 1）。
 *
 * Shell 的起始網址是 `https://app.local/index.html`（`WebViewBridge.IndexUri`），
 * 沒有 `/index.html → /` 這條 redirect 正式版一開起來就對不到任何路由。
 *
 * 拆成獨立檔案（不含 `createRouter`）是為了讓 `routes.test.ts` 能用
 * `createMemoryHistory` 驗證路由表本身，不必碰 `createWebHistory`（依賴 `document`，
 * 在 vitest 的 node 環境裡會直接丟例外）。
 */
import type { RouteRecordRaw } from 'vue-router'
import { currentYearMonth } from '@/composables/useYearMonth'

/**
 * `YYYY-MM`，跟 api-contract.yaml 的 `parameters.YearMonth.schema.pattern` 一致。
 *
 * 不能用 `(0[1-9]|1[0-2])` 這種帶巢狀括號的寫法——vue-router 的自訂 param 正規式
 * 在第一個未跳脫的 `)` 就結束擷取，巢狀括號會把正規式切斷變成 unterminated group，
 * `createRouter` 會直接丟例外（模組載入就死，整個 SPA 開不起來）。改成兩段整段
 * alternation，vue-router 會用外層的擷取群把兩段包起來，不會被提前截斷。
 */
export const YM_PARAM = ':ym(\\d{4}-0[1-9]|\\d{4}-1[0-2])'

export const routes: RouteRecordRaw[] = [
  { path: '/', redirect: () => `/schedules/${currentYearMonth()}` },
  { path: '/index.html', redirect: '/' },
  {
    path: `/schedules/${YM_PARAM}`,
    name: 'schedule',
    component: () => import('@/pages/schedule/index.vue'),
  },
  {
    path: '/settings/areas',
    name: 'areas',
    component: () => import('@/pages/areas/index.vue'),
  },
  {
    path: '/settings/constraints',
    name: 'constraints',
    component: () => import('@/pages/constraints/index.vue'),
  },
  {
    path: `/variants/${YM_PARAM}`,
    name: 'variants',
    component: () => import('@/pages/variants/index.vue'),
  },
  {
    path: `/blocked-days/${YM_PARAM}`,
    name: 'blockedDays',
    component: () => import('@/pages/blockedDays/index.vue'),
  },
  {
    path: '/staff',
    name: 'staff',
    component: () => import('@/pages/staff/index.vue'),
  },
  // 對不到任何路由（打錯字、舊書籤、外部連結失效）一律導回 `/`，
  // 再由上面那條 `/` 的 redirect 落到本月的排班主表，不要留一片空白畫面。
  { path: '/:pathMatch(.*)*', redirect: '/' },
]
