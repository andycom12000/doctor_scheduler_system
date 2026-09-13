/**
 * history 模式：Shell 的 `WebResourceRequested` 對無副檔名路徑一律回 `index.html`
 * （ARCHITECTURE §6.2），子路徑重新整理不會 404。
 *
 * 六條路由全部指向 `src/pages/<screen>/index.vue` 的佔位頁，畫面 PR 只改頁面內容，
 * 不改這份路由表（frontend-plan.md §4 的規則 1）。
 *
 * Shell 的起始網址是 `https://app.local/index.html`（`WebViewBridge.IndexUri`），
 * 沒有這條 redirect 正式版一開起來就對不到任何路由。
 */
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { currentYearMonth } from '@/composables/useYearMonth'

/** `YYYY-MM`，跟 api-contract.yaml 的 `parameters.YearMonth.schema.pattern` 一致。 */
const YM_PARAM = ':ym(\\d{4}-(0[1-9]|1[0-2]))'

const routes: RouteRecordRaw[] = [
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
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
})
