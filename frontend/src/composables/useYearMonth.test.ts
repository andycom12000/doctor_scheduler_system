import { createApp, type App } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { currentYearMonth, resetLastYearMonthForTests, useYearMonth } from './useYearMonth'

/**
 * 不用真正的 `src/router/routes.ts`——那份路由表的 `component` 是
 * `() => import('@/pages/.../index.vue')`，vue-router 在 `push()` 解析路由時
 * 就會去載入元件（不需要真的掛 `<RouterView>`），這裡的 vitest 設定沒有
 * `@vitejs/plugin-vue` 解析不了 `.vue`。測 `useYearMonth` 只需要「有沒有
 * `:ym` 這個路由參數」，用最小的假路由表就夠。
 */
function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/schedules/:ym(\\d{4}-0[1-9]|\\d{4}-1[0-2])', name: 'schedule', component: {} },
      { path: '/staff', name: 'staff', component: {} },
    ],
  })
}

/**
 * `useRoute`/`useRouter` 靠 inject 找路由實例，正常是在元件 `setup()` 裡呼叫；
 * 這裡沒有真的元件可掛，改用 `app.runWithContext`（Vue 3.3+）在同一個
 * injection context 下執行，效果等價。
 */
async function withRouter<T>(router: Router, initialPath: string, run: () => T): Promise<{ app: App; result: T }> {
  const app = createApp({})
  app.use(router)
  await router.push(initialPath)
  await router.isReady()
  const result = app.runWithContext(run)
  return { app, result }
}

beforeEach(() => {
  resetLastYearMonthForTests(new Date('2026-09-13T00:00:00'))
})

describe('useYearMonth', () => {
  it('路由有合法 :ym 時直接回傳它', async () => {
    const router = createTestRouter()
    const { result } = await withRouter(router, '/schedules/2026-12', () => useYearMonth())

    expect(result.ym.value).toBe('2026-12')
  })

  it('沒有 :ym 的路由回退到最後一個合法的 :ym，不是硬退回當月', async () => {
    const router = createTestRouter()
    const { result } = await withRouter(router, '/schedules/2026-12', () => useYearMonth())
    expect(result.ym.value).toBe('2026-12')

    await router.push('/staff')
    expect(result.ym.value).toBe('2026-12')
  })

  it('一開始就在沒有 :ym 的路由，回退到當月（模組初值）', async () => {
    const router = createTestRouter()
    const { result } = await withRouter(router, '/staff', () => useYearMonth())

    expect(result.ym.value).toBe(currentYearMonth(new Date('2026-09-13T00:00:00')))
  })

  it('setYearMonth 在有 :ym 的路由上原地替換', async () => {
    const router = createTestRouter()
    const { result } = await withRouter(router, '/schedules/2026-09', () => useYearMonth())

    result.setYearMonth('2026-11')
    await router.isReady()
    // router.replace 是非同步的，等一輪 microtask 讓 route 更新完成
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(router.currentRoute.value.path).toBe('/schedules/2026-11')
    expect(result.ym.value).toBe('2026-11')
  })

  it('setYearMonth 在沒有 :ym 的路由上導到排班主表', async () => {
    const router = createTestRouter()
    const { result } = await withRouter(router, '/staff', () => useYearMonth())

    result.setYearMonth('2026-11')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(router.currentRoute.value.name).toBe('schedule')
    expect(router.currentRoute.value.params.ym).toBe('2026-11')
  })
})
