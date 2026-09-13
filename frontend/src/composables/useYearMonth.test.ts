import { createApp, type App } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { currentYearMonth, resetLastYearMonthForTests, shiftYearMonth, useYearMonth, yearMonthOptions } from './useYearMonth'

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

describe('shiftYearMonth', () => {
  it('同年內加減', () => {
    expect(shiftYearMonth('2026-09', 1)).toBe('2026-10')
    expect(shiftYearMonth('2026-09', -1)).toBe('2026-08')
  })

  it('跨年進位／借位', () => {
    expect(shiftYearMonth('2026-12', 1)).toBe('2027-01')
    expect(shiftYearMonth('2026-01', -1)).toBe('2025-12')
  })

  it('delta 為 0 時原樣回傳', () => {
    expect(shiftYearMonth('2026-09', 0)).toBe('2026-09')
  })

  it('不符合 YEAR_MONTH_PATTERN 的輸入原樣回傳，不拋錯', () => {
    expect(shiftYearMonth('bogus', 1)).toBe('bogus')
  })

  it('三位數以下的年份不會被兩位數年份規則污染（new Date(50, 0, 1) 會變成 1950 年，這裡不能）', () => {
    expect(shiftYearMonth('0050-03', 1)).toBe('0050-04')
  })

  it('年份小到跨年借位時，依然是原樣的低位數年份，不是被兩位數規則救回來的 4 位數年份', () => {
    expect(shiftYearMonth('0001-01', -1)).toBe('0000-12')
  })
})

describe('yearMonthOptions', () => {
  it('以 baseYm 為中心，前後各 12 個月，含頭尾、依字串排序', () => {
    const options = yearMonthOptions('2026-09')
    expect(options).toHaveLength(25)
    expect(options[0]).toBe('2025-09')
    expect(options.at(-1)).toBe('2027-09')
    expect(options).toContain('2026-09')
  })

  it('extra 落在範圍外時一併列入', () => {
    const options = yearMonthOptions('2026-09', ['2020-01'])
    expect(options).toContain('2020-01')
    expect(options[0]).toBe('2020-01')
  })

  it('extra 落在範圍內時不會重複', () => {
    const options = yearMonthOptions('2026-09', ['2026-10'])
    expect(options.filter((option) => option === '2026-10')).toHaveLength(1)
  })
})
