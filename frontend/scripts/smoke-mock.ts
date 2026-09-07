/**
 * 本地驗證用：不進 CI。同一份斷言可以打兩個目標：
 *
 *   npm run mock:smoke                                   # MSW mock（預設，不需後端）
 *   npm run api:smoke                                    # 真後端 http://localhost:5080（先 dotnet run --project src/Scheduler.Api）
 *   npx tsx scripts/smoke-mock.ts --target http://host:port
 *
 * 兩邊都跑一次，mock 與後端的漂移直接看得到（docs/ARCHITECTURE.md §7）。
 * 依賴 mock 種子資料（34 人、2026-08／09 值班表）或尚未落地的寫入／求解端點的段落
 * 標為 mock-only，打真後端時略過；後端補上對應端點後把它們解鎖。
 *
 * 需要 `src/api/schema.d.ts` 存在，執行前請先 `npm run api:types` 或跑過一次 `npm run dev`/`typecheck`。
 */
import { setupServer } from 'msw/node'
import { handlers } from '../src/mocks/handlers'
import { resetStore } from '../src/mocks/store'

function parseTarget(argv: string[]): string {
  const eq = argv.find((a) => a.startsWith('--target='))
  if (eq) return eq.slice('--target='.length)
  const i = argv.indexOf('--target')
  if (i >= 0) {
    const value = argv[i + 1]
    if (!value) throw new Error('--target 後面要接 URL 或 mock')
    return value
  }
  return 'mock'
}

const target = parseTarget(process.argv.slice(2))
const isMock = target === 'mock'
const BASE = isMock ? 'http://mock.local/api' : `${target.replace(/\/$/, '')}/api`

if (isMock) {
  // Node 沒有瀏覽器的全域 `location`，但 handlers.ts 裡的 `http.get('/api/...')`
  // 是相對路徑（刻意如此，跟 frontend/src/api/client.ts 的 fetch('/api/...') 一致）。
  // msw 解析相對路徑時會讀 `location.href` 當 base；補一個最小的 shim 讓比對成立。
  ;(globalThis as unknown as { location: URL }).location = new URL('http://mock.local/')
}

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

let skipped = 0

/** 依賴 mock 種子資料或尚未落地的端點：打真後端時整段略過並註明原因。 */
async function mockOnly(title: string, reason: string, run: () => Promise<void>) {
  if (isMock) {
    console.log(title)
    await run()
  } else {
    skipped++
    console.log(`${title} — 略過（${reason}）`)
  }
}

async function main() {
  console.log(`目標：${isMock ? 'MSW mock' : target}`)
  const server = isMock ? setupServer(...handlers) : null
  if (server) {
    resetStore()
    server.listen({ onUnhandledRequest: 'error' })
  }

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

    }

    await mockOnly('2b. PATCH /calendars/2026/2026-11-05', '會在真後端留下一筆行事曆覆寫', async () => {
      // 刻意挑 seed 月（2026-08／2026-09）以外的日期——這支腳本後面還會斷言
      // 額度點數與公平性點數，若覆寫落在 seed 月內會汙染那些數字。
      const overridden = await fetch(`${BASE}/calendars/2026/2026-11-05`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isMakeUpWorkday: true, isHoliday: false }),
      })
      assert(overridden.status === 200, 'PATCH /calendars/2026/2026-11-05 → 200')
      const overriddenBody = await overridden.json()
      assert(overriddenBody.overridden === true, '覆寫後 overridden === true')
    })

    console.log('3. GET /staff')
    {
      const staff = await fetch(`${BASE}/staff`)
      assert(staff.status === 200, 'GET /staff → 200')
      const body = await staff.json()
      assert(body.items.length === body.counts.active + body.counts.inactive, 'items 數等於 counts.active + counts.inactive')
      if (!isMock && body.items.length === 0) {
        console.warn('  ! 真後端沒有人員資料：後面依資料量的斷言（byStaff、months）在空庫上恆真，鑑別力有限')
      }
      if (isMock) {
        assert(body.items.length === 34, 'staff.items.length === 34（33 醫師 + 1 NP）')
        assert(body.counts.active === 34, 'counts.active === 34')
      }
    }

    console.log('3b. 人員新增 → 編輯 → 停用 → 刪除（往返一圈，不留痕）')
    {
      const employeeNo = `SMOKE-${Date.now()}`
      const json = (body: unknown) => ({ headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      const created = await fetch(`${BASE}/staff`, { method: 'POST', ...json({ employeeNo, name: '煙霧測試', rankCode: 'R2' }) })
      assert(created.status === 201, 'POST /staff → 201')
      const staff = (await created.json()) as { id: string; status: string; eligibleAreaTypes: string[] }
      assert(staff.status === 'active', '新增後為在職')
      assert(staff.eligibleAreaTypes.includes('ICU'), 'R2 的可值類型由資格矩陣推出含 ICU')

      const dup = await fetch(`${BASE}/staff`, { method: 'POST', ...json({ employeeNo, name: '撞員編', rankCode: 'R3' }) })
      assert(dup.status === 409, '員編重複 → 409')
      assert(((await dup.json()) as { error: { code: string } }).error.code === 'EMPLOYEE_NO_TAKEN', '錯誤碼 EMPLOYEE_NO_TAKEN')

      const updated = await fetch(`${BASE}/staff/${staff.id}`, { method: 'PATCH', ...json({ employeeNo, name: '改名', rankCode: 'R4' }) })
      assert(updated.status === 200, 'PATCH /staff/:id → 200')
      const updatedBody = (await updated.json()) as { name: string; eligibleAreaTypes: string[] }
      assert(updatedBody.name === '改名' && updatedBody.eligibleAreaTypes.includes('CHIEF'), '換身分後可值類型重算')

      const inactive = await fetch(`${BASE}/staff/${staff.id}/status`, { method: 'PATCH', ...json({ status: 'inactive' }) })
      assert(inactive.status === 200 && ((await inactive.json()) as { status: string }).status === 'inactive', '停用 → inactive')

      const deleted = await fetch(`${BASE}/staff/${staff.id}`, { method: 'DELETE' })
      assert(deleted.status === 204, 'DELETE /staff/:id → 204')
      const gone = await fetch(`${BASE}/staff/${staff.id}`, { method: 'DELETE' })
      assert(gone.status === 404, '再刪一次 → 404')
    }

    console.log('4. GET /schedules')
    {
      const list = await fetch(`${BASE}/schedules`)
      assert(list.status === 200, 'GET /schedules → 200')
      const body = await list.json()
      assert(Array.isArray(body.months), 'months 是陣列')
      if (isMock) {
        assert(body.months.some((m: { yearMonth: string }) => m.yearMonth === '2026-08'), '清單含 2026-08')
        assert(body.months.some((m: { yearMonth: string }) => m.yearMonth === '2026-09'), '清單含 2026-09')
      }

      const missing = await fetch(`${BASE}/schedules/2099-01`)
      assert(missing.status === 404, 'GET 不存在的 ym → 404')
    }

    let revision = 0
    await mockOnly('5. GET /schedules/2026-09', '依賴 mock 種子的 2026-09 值班表', async () => {
      const res = await fetch(`${BASE}/schedules/2026-09`)
      assert(res.status === 200, 'GET /schedules/2026-09 → 200')
      const body = await res.json()
      assert(body.status === 'draft', '2026-09 初始狀態為 draft')
      assert(body.duties.length > 100, 'duties 數量合理（> 100）')
      revision = body.revision
    })

    await mockOnly('6. PATCH /schedules/2026-09/duties', '依賴 mock 種子的 2026-09 值班表', async () => {
      // 先從當天的值班表挑一位「9/10 沒班」的在職人員，避免撞到同人同日不變式
      const scheduleRes = await fetch(`${BASE}/schedules/2026-09`)
      const schedule = await scheduleRes.json()
      const onDuty = new Set(
        (schedule.duties as Array<{ date: string; staffId: string }>).filter((d) => d.date === '2026-09-10').map((d) => d.staffId),
      )
      const staffRes = await fetch(`${BASE}/staff?status=active`)
      const staff = ((await staffRes.json()) as { items: Array<{ id: string; rankCode: string }> }).items
      const free = staff.find((s) => !onDuty.has(s.id) && s.rankCode !== 'NP')
      assert(free, '找得到 9/10 沒班的人')
      const busy = [...onDuty][0]
      assert(busy, '9/10 至少有一人在班')

      const res = await fetch(`${BASE}/schedules/2026-09/duties`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ areaId: 'area-c', date: '2026-09-10', staffId: free.id }),
      })
      assert(res.status === 200, 'PATCH duties → 200')
      const body = await res.json()
      assert(body.revision === revision + 1, 'revision 遞增 1')
      assert(Array.isArray(body.violations), 'violations 是陣列')
      assert(body.duties.length === 1 && body.duties[0].staffId === free.id, 'duties 帶回改動的那一格')
      revision = body.revision

      // 同一人同一天已在另一區 → 409 STAFF_ALREADY_ON_DUTY（結構不變式，非約束）
      const conflict = await fetch(`${BASE}/schedules/2026-09/duties`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ areaId: 'area-c', date: '2026-09-10', staffId: busy }),
      })
      assert(conflict.status === 409, '同人同日另一區 → 409')
      const conflictBody = await conflict.json()
      assert(conflictBody.error.code === 'STAFF_ALREADY_ON_DUTY', '錯誤碼 STAFF_ALREADY_ON_DUTY')
      assert(typeof conflictBody.error.details?.areaId === 'string', 'details 帶他當天已在的 areaId')

      // 清空一格：回應仍帶那一格，staffId 為 null
      const clear = await fetch(`${BASE}/schedules/2026-09/duties`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ areaId: 'area-c', date: '2026-09-10', staffId: null }),
      })
      assert(clear.status === 200, '清空一格 → 200')
      const clearBody = await clear.json()
      assert(clearBody.duties.length === 1 && clearBody.duties[0].staffId === null, '清空後 duties 帶回該格且 staffId 為 null')
      revision = clearBody.revision
    })

    console.log('6b. 不存在的月份：validate / violations / point-board / days / vacancies / candidates 一律 404')
    {
      const paths: Array<readonly ['GET' | 'POST', string]> = [
        ['POST', '/schedules/2099-03/validate'],
        ['GET', '/schedules/2099-03/violations'],
        ['GET', '/schedules/2099-03/point-board'],
        ['GET', '/schedules/2099-03/days/2099-03-01'],
        ['GET', '/schedules/2099-03/vacancies'],
        ['GET', '/schedules/2099-03/candidates?areaId=area-a&date=2099-03-01'],
      ]
      if (isMock) paths.push(['POST', '/schedules/2099-03/publish'])
      for (const [method, path] of paths) {
        const res = await fetch(`${BASE}${path}`, { method })
        assert(res.status === 404, `${method} ${path} → 404`)
        const body = await res.json()
        assert(body?.error?.code === 'NOT_FOUND', `${method} ${path} 錯誤碼 NOT_FOUND`)
      }
    }

    await mockOnly('7. POST /schedules/2026-09/validate', '依賴 mock 種子的 2026-09 值班表', async () => {
      const res = await fetch(`${BASE}/schedules/2026-09/validate`, { method: 'POST' })
      assert(res.status === 200, 'POST validate → 200')
      const body = await res.json()
      assert(body.ok === false, '種子資料故意留違規，ok 應為 false')
      assert(body.summary.hard >= 1, '至少 1 條硬違規')
    })

    await mockOnly('8. GET /schedules/2026-09/point-board', '依賴 mock 種子的 2026-09 值班表', async () => {
      const res = await fetch(`${BASE}/schedules/2026-09/point-board`)
      assert(res.status === 200, 'GET point-board → 200')
      const body = await res.json()
      assert(body.groups.length === 4, 'point-board 分 4 組')
    })

    console.log('9. GET blocked-days / feasibility（不需要值班表存在）')
    const ym10 = '2026-10'
    {
      const registration = await fetch(`${BASE}/blocked-days/${ym10}`)
      assert(registration.status === 200, 'GET blocked-days → 200')
      const before = await registration.json()
      const activeCount = ((await (await fetch(`${BASE}/staff?status=active`)).json()) as { counts: { active: number } }).counts.active
      assert(before.byStaff.length === activeCount, 'byStaff 每位在職人員一列（0 也列）')
      assert(before.byStaff.every((r: { count: number }) => typeof r.count === 'number'), 'byStaff 每列都有 count')

      const feasibility = await fetch(`${BASE}/blocked-days/${ym10}/feasibility`)
      assert(feasibility.status === 200, 'GET feasibility → 200')
      const body = await feasibility.json()
      assert(body.bySupply.length === 3, 'bySupply 有三層巢狀累計')
    }

    console.log('9b. blocked-days PUT/DELETE（登記再清除，真後端不留痕）')
    {
      // 挑一位在職人員與一個他尚未登記的日子；真後端沒有人員時這段沒東西可打
      const active = ((await (await fetch(`${BASE}/staff?status=active`)).json()) as { items: Array<{ id: string }> }).items
      const staffId = active[0]?.id
      if (!staffId) {
        console.log('  （沒有在職人員，略過）')
      } else {
        const registration = (await (await fetch(`${BASE}/blocked-days/${ym10}`)).json()) as {
          entries: Array<{ staffId: string; date: string }>
          byStaff: Array<{ staffId: string; count: number }>
        }
        const taken = new Set(registration.entries.filter((e) => e.staffId === staffId).map((e) => e.date))
        const date = ['2026-10-05', '2026-10-06', '2026-10-07', '2026-10-08'].find((d) => !taken.has(d))
        assert(date, '找得到該人尚未登記的日子')
        const before = registration.byStaff.find((r) => r.staffId === staffId)?.count ?? 0

        const put = await fetch(`${BASE}/blocked-days/${ym10}/${staffId}/${date}`, { method: 'PUT' })
        assert(put.status === 200, 'PUT blocked-day → 200')
        const putBody = await put.json()
        assert(putBody.staffTotals.count === before + 1, '登記後 staffTotals.count 加 1')
        assert(typeof putBody.dateTotals.count === 'number', 'dateTotals.count 是數字')

        const del = await fetch(`${BASE}/blocked-days/${ym10}/${staffId}/${date}`, { method: 'DELETE' })
        assert(del.status === 200, 'DELETE blocked-day → 200')
        const delBody = await del.json()
        assert(delBody.staffTotals.count === before, '清除後回到原本的 count')

        const again = await fetch(`${BASE}/blocked-days/${ym10}/${staffId}/${date}`, { method: 'DELETE' })
        assert(again.status === 200, '未登記的格子再清一次仍 200（冪等）')
      }
    }

    let jobId = ''
    let variantId = ''
    await mockOnly('10. POST /solver-jobs → 輪詢至 succeeded', '求解端點尚未落地', async () => {
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
    })

    await mockOnly('11. GET /solver-jobs/:jobId/variants', '求解端點尚未落地', async () => {
      const res = await fetch(`${BASE}/solver-jobs/${jobId}/variants`)
      assert(res.status === 200, 'GET variants → 200')
      const body = await res.json()
      assert(body.variants.length === 3, '三份具名變體')
      variantId = body.variants[0].id
      const ids = body.variants.map((v: { id: string }) => v.id)
      assert(new Set(ids).size === 3, '三份變體 id 各不相同')
    })

    await mockOnly('12. POST /schedules/2026-10/apply-variant', '求解端點尚未落地', async () => {
      const res = await fetch(`${BASE}/schedules/${ym10}/apply-variant`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId, variantId }),
      })
      assert(res.status === 200, 'apply-variant → 200')
      const body = await res.json()
      assert(body.status === 'draft', '套用後為草稿')
      assert(body.duties.length > 100, '套用後有完整值班清單')
    })

    await mockOnly('13. POST /schedules/2026-10/publish', '依賴第 12 段套用的變體', async () => {
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
    })
  } finally {
    server?.close()
  }

  console.log(`\n${passed} passed, ${failed} failed${skipped ? `, ${skipped} sections skipped` : ''}`)
  if (failed > 0) process.exitCode = 1
}

void main()
