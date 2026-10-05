import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { handlers } from '@/mocks/handlers'
import { resetStore } from '@/mocks/store'
import type { ListStaffResponse } from '@/api/types'
import { hasNoActiveStaff } from './rosterGuard'

function response(active: number, inactive = 0): ListStaffResponse {
  return { items: [], counts: { active, inactive } }
}

describe('hasNoActiveStaff', () => {
  it('名冊還沒載入時回 null（不能當成 0 人，否則載入瞬間會閃引導）', () => {
    expect(hasNoActiveStaff(null)).toBeNull()
    expect(hasNoActiveStaff(undefined)).toBeNull()
  })

  it('counts.active 為 0 才算沒人', () => {
    expect(hasNoActiveStaff(response(0))).toBe(true)
    expect(hasNoActiveStaff(response(1))).toBe(false)
    expect(hasNoActiveStaff(response(33, 2))).toBe(false)
  })

  it('只有停用人員時仍算沒有在職人員', () => {
    expect(hasNoActiveStaff(response(0, 5))).toBe(true)
  })
})

describe('MSW 0 人情境', () => {
  const server = setupServer(...handlers)

  beforeAll(() => {
    // 與 scripts/smoke-mock.ts 相同：msw 解析相對路徑時要讀 location.href。
    ;(globalThis as unknown as { location: URL }).location = new URL('http://mock.local/')
    server.listen({ onUnhandledRequest: 'error' })
  })
  afterAll(() => server.close())
  beforeEach(() => resetStore())

  async function getStaff(): Promise<ListStaffResponse> {
    const res = await fetch('http://mock.local/api/staff')
    return (await res.json()) as ListStaffResponse
  }

  it('預設名冊有在職人員', async () => {
    expect(hasNoActiveStaff(await getStaff())).toBe(false)
  })

  it('resetStore({ noStaff: true }) 後 /api/staff 回 0 人，且當月沒有種子值班表', async () => {
    resetStore({ noStaff: true })
    const body = await getStaff()
    expect(body.items).toEqual([])
    expect(body.counts).toEqual({ active: 0, inactive: 0 })
    expect(hasNoActiveStaff(body)).toBe(true)

    const schedules = await fetch('http://mock.local/api/schedules')
    expect(((await schedules.json()) as { months: unknown[] }).months).toEqual([])
  })

  it('新增第一個人之後 counts.active 變 1（頁面靠 invalidate("staff") 切回一般空狀態）', async () => {
    resetStore({ noStaff: true })
    const created = await fetch('http://mock.local/api/staff', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        employeeNo: 'N001',
        name: '測試醫師',
        rankCode: 'PGY1',
      }),
    })
    expect(created.status).toBe(201)
    expect(hasNoActiveStaff(await getStaff())).toBe(false)
  })
})
