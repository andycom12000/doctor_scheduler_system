/**
 * 本地驗證用：不進 CI。用 `msw/node` 掛上 `src/mocks/handlers.ts`，
 * 跑一輪主要流程，斷言狀態碼與關鍵欄位，確認 mock 在沒有真後端的情況下
 * 端到端可用。
 *
 * 執行：`npm run mock:smoke`（package.json 已設定 `pretypecheck`/`predev` 等
 * hook 會先跑 `api:types`，這支腳本本身也需要 `src/api/schema.d.ts` 存在，
 * 執行前請先 `npm run api:types` 或跑過一次 `npm run dev`/`typecheck`）。
 */
import { setupServer } from 'msw/node'
import { handlers } from '../src/mocks/handlers'
import { resetStore } from '../src/mocks/store'

const BASE = 'http://mock.local/api'

// Node 沒有瀏覽器的全域 `location`，但 handlers.ts 裡的 `http.get('/api/...')`
// 是相對路徑（刻意如此，跟 frontend/src/api/client.ts 的 fetch('/api/...') 一致）。
// msw 解析相對路徑時會讀 `location.href` 當 base；補一個最小的 shim 讓比對成立。
;(globalThis as unknown as { location: URL }).location = new URL('http://mock.local/')

let passed = 0
let failed = 0

function assert(condition: unknown, message: string): asserts condition {
  if (condition) {
    passed++
    console.log(`  ✓ ${message}`)
  } else {
    failed++
    console.error(`  ✗ ${message}`)
  }
}

async function main() {
  resetStore()
  const server = setupServer(...handlers)
  server.listen({ onUnhandledRequest: 'error' })

  try {
    console.log('1. GET /settings/*')
    {
      const areas = await fetch(`${BASE}/settings/areas`)
      assert(areas.status === 200, 'GET /settings/areas → 200')
      const areasBody = await areas.json()
      assert(areasBody.areas.length === 5, 'areas.length === 5')
      assert(areasBody.areaTypes.length === 3, 'areaTypes.length === 3')

      const ranks = await fetch(`${BASE}/settings/ranks`)
      assert(ranks.status === 200, 'GET /settings/ranks → 200')
      const ranksBody = await ranks.json()
      assert(ranksBody.ranks.length === 10, 'ranks.length === 10')
      assert(ranksBody.groups.length === 4, 'groups.length === 4')

      const pointRules = await fetch(`${BASE}/settings/point-rules`)
      assert(pointRules.status === 200, 'GET /settings/point-rules → 200')

      const constraints = await fetch(`${BASE}/settings/constraints`)
      const constraintsBody = await constraints.json()
      assert(constraintsBody.hard.length === 7, 'constraints.hard.length === 7')
      assert(constraintsBody.soft.length === 7, 'constraints.soft.length === 7')

      const eligibility = await fetch(`${BASE}/settings/eligibility-matrix`)
      assert(eligibility.status === 200, 'GET /settings/eligibility-matrix → 200')
    }

    console.log('2. GET /calendars/2026')
    {
      const calendar = await fetch(`${BASE}/calendars/2026`)
      assert(calendar.status === 200, 'GET /calendars/2026 → 200')
      const body = await calendar.json()
      assert(body.days.length === 365, 'calendar 2026 有 365 天')

      const overridden = await fetch(`${BASE}/calendars/2026/2026-09-05`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isMakeUpWorkday: true, isHoliday: false }),
      })
      assert(overridden.status === 200, 'PATCH /calendars/2026/2026-09-05 → 200')
      const overriddenBody = await overridden.json()
      assert(overriddenBody.overridden === true, '覆寫後 overridden === true')
    }

    console.log('3. GET /staff')
    {
      const staff = await fetch(`${BASE}/staff`)
      assert(staff.status === 200, 'GET /staff → 200')
      const body = await staff.json()
      assert(body.items.length === 34, 'staff.items.length === 34（33 醫師 + 1 NP）')
      assert(body.counts.active === 34, 'counts.active === 34')
    }

    console.log('4. GET /schedules')
    {
      const list = await fetch(`${BASE}/schedules`)
      assert(list.status === 200, 'GET /schedules → 200')
      const body = await list.json()
      assert(body.months.some((m: { yearMonth: string }) => m.yearMonth === '2026-08'), '清單含 2026-08')
      assert(body.months.some((m: { yearMonth: string }) => m.yearMonth === '2026-09'), '清單含 2026-09')

      const missing = await fetch(`${BASE}/schedules/2099-01`)
      assert(missing.status === 404, 'GET 不存在的 ym → 404')
    }

    console.log('5. GET /schedules/2026-09')
    let revision = 0
    {
      const res = await fetch(`${BASE}/schedules/2026-09`)
      assert(res.status === 200, 'GET /schedules/2026-09 → 200')
      const body = await res.json()
      assert(body.status === 'draft', '2026-09 初始狀態為 draft')
      assert(body.duties.length > 100, 'duties 數量合理（> 100）')
      revision = body.revision
    }

    console.log('6. PATCH /schedules/2026-09/duties')
    {
      const res = await fetch(`${BASE}/schedules/2026-09/duties`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ areaId: 'area-c', date: '2026-09-10', staffId: 'staff-001' }),
      })
      assert(res.status === 200, 'PATCH duties → 200')
      const body = await res.json()
      assert(body.revision === revision + 1, 'revision 遞增 1')
      assert(Array.isArray(body.violations), 'violations 是陣列')
    }

    console.log('7. POST /schedules/2026-09/validate')
    {
      const res = await fetch(`${BASE}/schedules/2026-09/validate`, { method: 'POST' })
      assert(res.status === 200, 'POST validate → 200')
      const body = await res.json()
      assert(body.ok === false, '種子資料故意留違規，ok 應為 false')
      assert(body.summary.hard >= 1, '至少 1 條硬違規')
    }

    console.log('8. GET /schedules/2026-09/point-board')
    {
      const res = await fetch(`${BASE}/schedules/2026-09/point-board`)
      assert(res.status === 200, 'GET point-board → 200')
      const body = await res.json()
      assert(body.groups.length === 4, 'point-board 分 4 組')
    }

    console.log('9. blocked-days PUT/feasibility')
    let ym10 = '2026-10'
    {
      const put = await fetch(`${BASE}/blocked-days/${ym10}/staff-002/2026-10-05`, { method: 'PUT' })
      assert(put.status === 200, 'PUT blocked-day → 200')
      const putBody = await put.json()
      assert(putBody.staffTotals.count === 1, '登記後 count === 1')

      const feasibility = await fetch(`${BASE}/blocked-days/${ym10}/feasibility`)
      assert(feasibility.status === 200, 'GET feasibility → 200')
      const body = await feasibility.json()
      assert(body.bySupply.length === 3, 'bySupply 有三層巢狀累計')

      const del = await fetch(`${BASE}/blocked-days/${ym10}/staff-002/2026-10-05`, { method: 'DELETE' })
      assert(del.status === 200, 'DELETE blocked-day → 200')
    }

    console.log('10. POST /solver-jobs → 輪詢至 succeeded')
    let jobId = ''
    {
      const res = await fetch(`${BASE}/solver-jobs`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ yearMonth: ym10, variantCount: 3, timeLimitSecPerVariant: 5 }),
      })
      assert(res.status === 202, 'POST /solver-jobs → 202')
      const body = await res.json()
      jobId = body.jobId
      assert(typeof jobId === 'string' && jobId.length > 0, 'jobId 非空字串')

      const busy = await fetch(`${BASE}/solver-jobs`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ yearMonth: ym10 }),
      })
      assert(busy.status === 409, '同時建立第二個工作 → 409 SOLVER_BUSY')

      let status = 'queued'
      const deadline = Date.now() + 20_000
      while (status !== 'succeeded' && Date.now() < deadline) {
        await new Promise((resolve) => setTimeout(resolve, 500))
        const poll = await fetch(`${BASE}/solver-jobs/${jobId}`)
        const pollBody = await poll.json()
        status = pollBody.status
      }
      assert(status === 'succeeded', `求解工作在期限內完成（最終狀態：${status}）`)
    }

    console.log('11. GET /solver-jobs/:jobId/variants')
    let variantId = ''
    {
      const res = await fetch(`${BASE}/solver-jobs/${jobId}/variants`)
      assert(res.status === 200, 'GET variants → 200')
      const body = await res.json()
      assert(body.variants.length === 3, '三份具名變體')
      variantId = body.variants[0].id
      const ids = body.variants.map((v: { id: string }) => v.id)
      assert(new Set(ids).size === 3, '三份變體 id 各不相同')
    }

    console.log('12. POST /schedules/2026-10/apply-variant')
    {
      const res = await fetch(`${BASE}/schedules/${ym10}/apply-variant`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId, variantId }),
      })
      assert(res.status === 200, 'apply-variant → 200')
      const body = await res.json()
      assert(body.status === 'draft', '套用後為草稿')
      assert(body.duties.length > 100, '套用後有完整值班清單')
    }

    console.log('13. POST /schedules/2026-10/publish')
    {
      const res = await fetch(`${BASE}/schedules/${ym10}/publish`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ acknowledgeViolations: true }),
      })
      assert(res.status === 200, 'publish（acknowledgeViolations）→ 200')
      const body = await res.json()
      assert(body.status === 'published', '發布後狀態為 published')

      const reapply = await fetch(`${BASE}/schedules/${ym10}/apply-variant`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId, variantId }),
      })
      assert(reapply.status === 409, '對已發布值班表套用變體 → 409')
    }
  } finally {
    server.close()
  }

  console.log(`\n${passed} passed, ${failed} failed`)
  if (failed > 0) process.exitCode = 1
}

void main()
