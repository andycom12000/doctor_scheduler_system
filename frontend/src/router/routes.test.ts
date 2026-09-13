import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it } from 'vitest'
import { routes } from './routes'

/**
 * 用 `createMemoryHistory` 而不是 `createWebHistory`：後者會碰 `document`，
 * vitest 這份設定是 `environment: 'node'`，沒有 DOM。`resolve()` 不會執行
 * 路由表裡 `component: () => import('@/pages/.../index.vue')` 的 lazy import，
 * 所以這裡不需要 `@vitejs/plugin-vue` 就能測。
 */
function createTestRouter() {
  return createRouter({ history: createMemoryHistory(), routes })
}

describe('router routes', () => {
  it('路由表本身合法，createRouter 不會丟例外', () => {
    expect(() => createTestRouter()).not.toThrow()
  })

  it('/schedules/:ym 解析出 ym 參數', () => {
    const router = createTestRouter()
    const resolved = router.resolve('/schedules/2026-09')
    expect(resolved.name).toBe('schedule')
    expect(resolved.params.ym).toBe('2026-09')
  })

  it('月份超過 12 的 :ym 不會被 schedule 路由吃到，落到 catch-all 導回 /', () => {
    const router = createTestRouter()
    const resolved = router.resolve('/schedules/2026-13')
    expect(resolved.name).toBeUndefined()
    expect(resolved.matched[0]?.redirect).toBe('/')
  })

  it('對不到的路徑（打錯字、失效連結）落到 catch-all 導回 /', () => {
    const router = createTestRouter()
    const resolved = router.resolve('/no/such/path')
    expect(resolved.matched[0]?.redirect).toBe('/')
  })

  it('/blocked-days/:ym 解析出 blockedDays 路由', () => {
    const router = createTestRouter()
    const resolved = router.resolve('/blocked-days/2026-09')
    expect(resolved.name).toBe('blockedDays')
    expect(resolved.params.ym).toBe('2026-09')
  })

  it('/variants/:ym 解析出 variants 路由', () => {
    const router = createTestRouter()
    const resolved = router.resolve('/variants/2026-01')
    expect(resolved.name).toBe('variants')
    expect(resolved.params.ym).toBe('2026-01')
  })

  it('/settings/areas 與 /settings/constraints 各自解析', () => {
    const router = createTestRouter()
    expect(router.resolve('/settings/areas').name).toBe('areas')
    expect(router.resolve('/settings/constraints').name).toBe('constraints')
  })

  it('/staff 解析出 staff 路由', () => {
    const router = createTestRouter()
    expect(router.resolve('/staff').name).toBe('staff')
  })

  it('/index.html 導向 /', () => {
    const router = createTestRouter()
    const record = router.resolve('/index.html').matched[0]
    expect(record.redirect).toBe('/')
  })

  it('/ 導向本月的 /schedules/:ym', () => {
    const router = createTestRouter()
    const record = router.resolve('/').matched[0]
    expect(typeof record.redirect).toBe('function')
  })
})
