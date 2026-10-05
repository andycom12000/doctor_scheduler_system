/**
 * 「空月份不留痕」（issue #47 SCREEN 01 第 1 項）：
 *
 * - 沒有值班表的月份（`GET /schedules/{ym}` 404）畫面是 EmptyState，矩陣骨架不可點（#62），
 *   點不到任何格。
 * - 就算從別的路徑開了候選人面板，面板只讀（`GET candidates`），取消／關閉不寫任何東西；
 *   草稿只會在使用者明確選了人（`PATCH duties`）那一刻才建立。
 *
 * 這支測試守住的是後端語意那一側：讀取端點不得建草稿，`setDuty` 自動建草稿的行為不變。
 * （元件層的「骨架不可點」沒有元件測試設施，由 EmptyState.vue 全用 `<div>` 與瀏覽器驗證守。）
 */
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { handlers } from '@/mocks/handlers'
import { resetStore, store } from '@/mocks/store'

const BASE = 'http://mock.local'
const YM = '2027-02'

describe('空月份不留痕', () => {
  const server = setupServer(...handlers)

  beforeAll(() => {
    ;(globalThis as unknown as { location: URL }).location = new URL(`${BASE}/`)
    server.listen({ onUnhandledRequest: 'error' })
  })
  afterAll(() => server.close())
  beforeEach(() => resetStore())

  async function months(): Promise<string[]> {
    const res = await fetch(`${BASE}/api/schedules`)
    return ((await res.json()) as { months: { yearMonth: string }[] }).months.map((m) => m.yearMonth)
  }

  it('讀取候選人、違規、空缺、點數看板、單日詳表都不會建草稿', async () => {
    const area = store.areas[0]!
    expect(await months()).not.toContain(YM)

    const reads = [
      `/api/schedules/${YM}/candidates?areaId=${area.id}&date=${YM}-03`,
      `/api/schedules/${YM}/violations`,
      `/api/schedules/${YM}/vacancies`,
      `/api/schedules/${YM}/point-board`,
      `/api/schedules/${YM}/days/${YM}-03`,
    ]
    for (const path of reads) {
      const res = await fetch(`${BASE}${path}`)
      expect(res.status, path).toBe(404)
    }

    expect((await fetch(`${BASE}/api/schedules/${YM}`)).status).toBe(404)
    expect(await months()).not.toContain(YM)
  })

  it('明確指派（PATCH duties）才建草稿，語意不變：revision 變 1、狀態 draft', async () => {
    const area = store.areas[0]!
    const staff = store.staff.find((s) => s.status === 'active')!
    const res = await fetch(`${BASE}/api/schedules/${YM}/duties`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ areaId: area.id, date: `${YM}-03`, staffId: staff.id }),
    })
    expect(res.status).toBe(200)
    expect(await months()).toContain(YM)

    const schedule = (await (await fetch(`${BASE}/api/schedules/${YM}`)).json()) as {
      status: string
      revision: number
    }
    expect(schedule.status).toBe('draft')
    expect(schedule.revision).toBe(1)
  })
})
