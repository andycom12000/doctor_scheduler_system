/**
 * 本地驗證用：不進 CI。同一份斷言可以打兩個目標：
 *
 *   npm run mock:smoke                                   # MSW mock（預設，不需後端）
 *   npm run api:smoke                                    # 真後端 http://localhost:5080（先 dotnet run --project src/Scheduler.Api）
 *   npx tsx scripts/smoke-mock.ts --target http://host:port
 *
 * 兩邊都跑一次，mock 與後端的漂移直接看得到（docs/ARCHITECTURE.md §7）。
 * 依賴 mock 種子資料（2026-08／09 值班表）或尚未落地的寫入／求解端點的段落
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

    console.log('2a. GET /calendars/2027')
    {
      const res = await fetch(`${BASE}/calendars/2027`)
      assert(res.status === 200, 'GET /calendars/2027 → 200')
      const body = await res.json()
      const publicHolidays = body.days.filter((d: { isPublicHoliday: boolean }) => d.isPublicHoliday)
      assert(publicHolidays.length === 24, 'calendar 2027 國定假日 24 天')
      assert(!body.days.some((d: { isMakeUpWorkday: boolean }) => d.isMakeUpWorkday), 'calendar 2027 沒有補班日')
      const newYear = body.days.find((d: { date: string }) => d.date === '2027-01-01')
      assert(newYear?.holidayName === '元旦', '2027-01-01 元旦')
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
      const rosterIntact = body.items.length === 34 && body.counts.active === 34
      if (isMock) {
        assert(rosterIntact, 'staff.items.length === 34（33 醫師 + 1 NP）且 counts.active === 34')
      } else if (!rosterIntact) {
        // 真後端的名冊會被 3b 這類測試改動（新增／停用／刪除），之後的 agent 一旦對真後端
        // 寫入人員，這條硬斷就會誤報成後端壞掉，所以在真後端只警告、不判失敗。
        console.warn('名冊已被改過，34 人斷言略過')
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

      // issue #40：PATCH 改成別人的員編 → 409（排除自己，PATCH 保留原員編不算重複，見下方 updated 那次）
      const buddyNo = `SMOKE-${Date.now()}-B`
      const buddyCreated = await fetch(`${BASE}/staff`, { method: 'POST', ...json({ employeeNo: buddyNo, name: '陪測用', rankCode: 'R2' }) })
      assert(buddyCreated.status === 201, '建立陪測人員 → 201')
      const buddy = (await buddyCreated.json()) as { id: string }

      const patchConflict = await fetch(`${BASE}/staff/${staff.id}`, { method: 'PATCH', ...json({ employeeNo: buddyNo, name: '改名', rankCode: 'R4' }) })
      assert(patchConflict.status === 409, 'PATCH 改成別人的員編 → 409')
      assert(
        ((await patchConflict.json()) as { error: { code: string } }).error.code === 'EMPLOYEE_NO_TAKEN',
        'PATCH 409 錯誤碼 EMPLOYEE_NO_TAKEN',
      )

      const patchBlankNo = await fetch(`${BASE}/staff/${staff.id}`, { method: 'PATCH', ...json({ employeeNo: '  ', name: '改名', rankCode: 'R4' }) })
      assert(patchBlankNo.status === 422, 'PATCH 員編空白 → 422')
      assert(
        ((await patchBlankNo.json()) as { error: { code: string } }).error.code === 'INVALID_REQUEST',
        'PATCH 員編空白 錯誤碼 INVALID_REQUEST',
      )

      const patchBadRank = await fetch(`${BASE}/staff/${staff.id}`, { method: 'PATCH', ...json({ employeeNo, name: '改名', rankCode: 'NOPE' }) })
      assert(patchBadRank.status === 422, 'PATCH 不存在的身分 → 422')

      const buddyDeleted = await fetch(`${BASE}/staff/${buddy.id}`, { method: 'DELETE' })
      assert(buddyDeleted.status === 204, '清掉陪測人員 → 204')

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
      assert(body.months.every((m: { publishedVersion: unknown }) => typeof m.publishedVersion === 'number'), '每個月份都帶 publishedVersion')
      assert(
        body.months.every((m: { editedSincePublish: unknown }) => typeof m.editedSincePublish === 'boolean'),
        '每個月份都帶 editedSincePublish',
      )
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
      assert(body.publishedVersion === 0, '草稿 publishedVersion = 0')
      assert(body.editedSincePublish === false, '草稿 editedSincePublish = false')
      assert(body.duties.length > 100, 'duties 數量合理（> 100）')
      revision = body.revision
    })

    await mockOnly('6. PATCH /schedules/2026-09/duties', '依賴 mock 種子的 2026-09 值班表', async () => {
      // 先從當天的值班表挑一位「9/10 沒班」的在職人員，避免造出同人同日兩區（X1）而干擾後面的斷言
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

      // 同一人同一天已在另一區 → 照常寫入 200，違規清單出現 X1（硬違規，兩格都標；#68）
      const conflict = await fetch(`${BASE}/schedules/2026-09/duties`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ areaId: 'area-c', date: '2026-09-10', staffId: busy }),
      })
      assert(conflict.status === 200, '同人同日另一區 → 200（寫入不擋）')
      const conflictBody = await conflict.json()
      const x1 = (conflictBody.violations as Array<{ code: string; severity: string; cellKeys: string[] }>).find(
        (v) => v.code === 'X1_STAFF_DOUBLE_BOOKED' && v.cellKeys.includes('area:area-c:2026-09-10'),
      )
      assert(x1, '違規清單出現 X1_STAFF_DOUBLE_BOOKED')
      assert(x1.severity === 'hard', 'X1 是硬違規')
      assert(x1.cellKeys.length === 2, 'X1 的 cellKeys 含兩個 area 格')
      revision = conflictBody.revision

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
      assert(body.baselineBySupply.length === 3, 'baselineBySupply（零登記基準）也是三層')
      assert(
        body.baselineBySupply.every((t: { demandPoints: number }, i: number) => t.demandPoints === body.bySupply[i].demandPoints),
        '基準與目前的需求點數相同（只有供給受登記影響）',
      )
    }

    console.log('9b. blocked-days PUT/DELETE（登記再清除，真後端不留痕）')
    {
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

    console.log('9c. issue #20：不依賴種子、不落資料的錯誤碼檢查（mock 與真後端都跑）')
    {
      // 不存在的 jobId → 404 NOT_FOUND（不是 200 空陣列）。不需要真的建過求解工作。
      const missing = await fetch(`${BASE}/solver-jobs/job-does-not-exist/variants`)
      assert(missing.status === 404, 'GET /solver-jobs/job-does-not-exist/variants → 404')
      const missingBody = await missing.json()
      assert(missingBody.error?.code === 'NOT_FOUND', '錯誤碼 NOT_FOUND')

      // 本體只給 jobId、缺 variantId → 422 INVALID_REQUEST。真後端的
      // RequestMapper.Required(dto.VariantId) 在碰資料庫前就丟，jobId 是不是真的存在無所謂。
      const missingField = await fetch(`${BASE}/schedules/${ym10}/apply-variant`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId: 'no-such-job' }),
      })
      assert(missingField.status === 422, 'POST apply-variant 本體缺 variantId → 422')
      assert((await missingField.json()).error?.code === 'INVALID_REQUEST', '錯誤碼 INVALID_REQUEST（缺欄位）')
    }

    let jobId = ''
    let variantId = ''
    await mockOnly('10. POST /solver-jobs → 輪詢至 succeeded', '會在真後端留下求解紀錄（依設計全部保留）', async () => {
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

    await mockOnly('11. GET /solver-jobs/:jobId/variants', '依賴第 10 段的工作', async () => {
      const res = await fetch(`${BASE}/solver-jobs/${jobId}/variants`)
      assert(res.status === 200, 'GET variants → 200')
      const body = await res.json()
      assert(body.variants.length === 3, '三份具名變體')
      variantId = body.variants[0].id
      const ids = body.variants.map((v: { id: string }) => v.id)
      assert(new Set(ids).size === 3, '三份變體 id 各不相同')
      // 不存在的 jobId → 404 NOT_FOUND 已移到不依賴種子的 9c 段，兩邊都跑。
    })

    await mockOnly('12. POST /schedules/2026-10/apply-variant', '依賴第 11 段的變體', async () => {
      const res = await fetch(`${BASE}/schedules/${ym10}/apply-variant`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId, variantId }),
      })
      assert(res.status === 200, 'apply-variant → 200')
      const body = await res.json()
      assert(body.status === 'draft', '套用後為草稿')
      assert(body.duties.length > 100, '套用後有完整值班清單')
      // 本體缺 variantId → 422 已移到不依賴種子的 9c 段，兩邊都跑。

      // issue #20：變體所屬月份（2026-10）與路徑 ym（2026-09）不同 → 422 INVALID_REQUEST
      const wrongMonth = await fetch(`${BASE}/schedules/2026-09/apply-variant`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobId, variantId }),
      })
      assert(wrongMonth.status === 422, '變體所屬月份與路徑 ym 不同 → 422')
      assert((await wrongMonth.json()).error?.code === 'INVALID_REQUEST', '錯誤碼 INVALID_REQUEST（跨月套用）')
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

    console.log('14. GET /schedules/:ym/export')
    {
      const missing = await fetch(`${BASE}/schedules/2030-01/export`)
      assert(missing.status === 404, '尚無值班表的月份匯出 → 404')
      const bad = await fetch(`${BASE}/schedules/2030-01/export?layout=by-magic`)
      assert(bad.status === 422, 'layout 不合法 → 422')
      const empty = await fetch(`${BASE}/schedules/2030-01/export?layout=`)
      assert(empty.status === 422, 'layout 空字串不算沒給 → 422')
    }
    await mockOnly('14b. GET /schedules/2026-09/export 本體', '依賴 mock 種子的 2026-09 值班表', async () => {
      const res = await fetch(`${BASE}/schedules/2026-09/export?layout=day-by-staff`)
      assert(res.status === 200, '匯出 → 200')
      assert(
        res.headers.get('content-type')?.startsWith('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet') === true,
        'Content-Type 是 xlsx',
      )
      assert(res.headers.get('content-disposition')?.includes('duty-2026-09.xlsx') === true, 'Content-Disposition 帶檔名')
      const bytes = new Uint8Array(await res.arrayBuffer())
      assert(bytes.length > 0, '本體非空') // mock 回的是 CSV 佔位；真 xlsx 的位元組在 Api 測試裡驗
    })

    // 15. 值班表寫入流程（issue #34）。放在最後：會把 2026-09 發布，前面那些段落斷言的
    // 是草稿狀態的種子資料。打真後端會留下資料（發布無法復原），所以 mock-only。
    await mockOnly('15. 值班表寫入：setDuty／swap／validate／publish／export', '會把真後端的 2026-09 發布', async () => {
      const json = { 'Content-Type': 'application/json' }
      const scheduleBody = async () => (await fetch(`${BASE}/schedules/2026-09`)).json()
      const dutyAt = (s: { duties: Array<{ areaId: string; date: string; staffId: string }> }, areaId: string, date: string) =>
        s.duties.find((d) => d.areaId === areaId && d.date === date)?.staffId ?? null

      type Cell = { areaId: string; date: string }
      /** 某人 d1 在 X 區、d2 在 Y 區：a = (X, d1)、b = d2 的另一區（不是 Y）。優先挑 X === Y。 */
      const findCrossDateConflict = (s: { duties: Array<{ areaId: string; date: string; staffId: string | null }> }) => {
        const areas = [...new Set(s.duties.map((d) => d.areaId))]
        const assigned = s.duties.filter((d) => d.staffId)
        let fallback: { a: Cell; b: Cell; label: string; staffId: string; clashArea: string } | null = null
        for (const x of assigned) {
          for (const y of assigned) {
            if (y.staffId !== x.staffId || y.date === x.date) continue
            const z = areas.find((id) => id !== y.areaId)
            if (!z) continue
            const pick = {
              a: { areaId: x.areaId, date: x.date },
              b: { areaId: z, date: y.date },
              label: x.areaId === y.areaId ? '同區別天' : '別區別天',
              staffId: x.staffId as string,
              clashArea: y.areaId,
            }
            if (x.areaId === y.areaId) return pick
            fallback ??= pick
          }
        }
        return fallback
      }

      // setDuty 一格 → 清空：清空的格子要帶回 staffId: null
      const before = await scheduleBody()
      assert(before.status === 'draft', '2026-09 起始為 draft')
      const original = dutyAt(before, 'area-a', '2026-09-03')
      assert(original !== null, '2026-09-03 area-a 有人')
      const cleared = await fetch(`${BASE}/schedules/2026-09/duties`, {
        method: 'PATCH',
        headers: json,
        body: JSON.stringify({ areaId: 'area-a', date: '2026-09-03', staffId: null }),
      })
      assert(cleared.status === 200, 'setDuty 清空 → 200')
      const clearedBody = await cleared.json()
      assert(clearedBody.duties[0].staffId === null, '清空的格子帶回 staffId: null')
      assert(
        clearedBody.violations.some((v: { code: string }) => v.code === 'H1_AREA_COVERAGE'),
        '清空後出現 H1 空缺違規（發布會因此被擋）',
      )

      // swap 跨日撞同人同日：某人 d1 在 X 區、d2 在 Y 區，把 (X, d1) 拖到 d2 的另一區 → 照常寫入 200，
      // 違規清單出現 X1（#68）；發布帶 ack 仍 409 DOUBLE_BOOKING_PRESENT；對調回去排除後 X1 消失。
      // 同區別天（X === Y）是 mock 曾經漏判的情境（只比區域就把它當成來源格），優先挑它。
      const beforeConflict = await scheduleBody()
      const conflict = findCrossDateConflict(beforeConflict)
      assert(conflict !== null, '找得到跨日對調會撞到同人同日的格子')
      if (conflict) {
        // 退到「別區別天」測試照樣會過，但就不再守原本那個 bug——種子變了要知道。
        assert(conflict.label === '同區別天', '挑到的是同區別天（原本漏判的情境）')
        const swapBody = JSON.stringify({ a: conflict.a, b: conflict.b })
        const res = await fetch(`${BASE}/schedules/2026-09/duties/swap`, { method: 'POST', headers: json, body: swapBody })
        assert(res.status === 200, `跨日對調撞同人同日 → 200，寫入不擋（${conflict.label}）`)
        const swapResult = await res.json()
        const x1 = (swapResult.violations as Array<{ code: string; severity: string; cellKeys: string[] }>).find(
          (v) =>
            v.code === 'X1_STAFF_DOUBLE_BOOKED' &&
            v.cellKeys.includes(`area:${conflict.b.areaId}:${conflict.b.date}`) &&
            v.cellKeys.includes(`area:${conflict.clashArea}:${conflict.b.date}`),
        )
        assert(x1, '違規清單出現 X1，兩個相關格都在 cellKeys')
        assert(x1.severity === 'hard', 'X1 是硬違規')
        assert(
          dutyAt(await scheduleBody(), conflict.b.areaId, conflict.b.date) === conflict.staffId,
          '被拖的人確實落到目標格',
        )

        for (const ack of [false, true]) {
          const blockedPublish = await fetch(`${BASE}/schedules/2026-09/publish`, {
            method: 'POST',
            headers: json,
            body: JSON.stringify({ acknowledgeViolations: ack }),
          })
          assert(blockedPublish.status === 409, `有 X1 發布（ack=${ack}）→ 409`)
          assert((await blockedPublish.json()).error?.code === 'DOUBLE_BOOKING_PRESENT', '錯誤碼 DOUBLE_BOOKING_PRESENT')
        }

        const blockedExport = await fetch(`${BASE}/schedules/2026-09/export?layout=area-by-day`)
        assert(blockedExport.status === 409, '有 X1 匯出 → 409')
        assert((await blockedExport.json()).error?.code === 'DOUBLE_BOOKING_PRESENT', '匯出錯誤碼 DOUBLE_BOOKING_PRESENT')

        // 再對調一次（swap 是對合）把重複排除
        const undo = await fetch(`${BASE}/schedules/2026-09/duties/swap`, { method: 'POST', headers: json, body: swapBody })
        assert(undo.status === 200, '對調回去 → 200')
        assert(
          !(await undo.json()).violations.some((v: { code: string }) => v.code === 'X1_STAFF_DOUBLE_BOOKED'),
          '排除後違規清單沒有 X1',
        )
      }

      const sameCell = await fetch(`${BASE}/schedules/2026-09/duties/swap`, {
        method: 'POST',
        headers: json,
        body: JSON.stringify({ a: { areaId: 'area-a', date: '2026-09-04' }, b: { areaId: 'area-a', date: '2026-09-04' } }),
      })
      assert(sameCell.status === 422, '對調同一格 → 422')

      // swap：同一天兩區對調
      const staffA = dutyAt(before, 'area-a', '2026-09-04')
      const staffB = dutyAt(before, 'area-b', '2026-09-04')
      assert(staffA !== null && staffB !== null, '2026-09-04 area-a／area-b 都有人')
      const swapped = await fetch(`${BASE}/schedules/2026-09/duties/swap`, {
        method: 'POST',
        headers: json,
        body: JSON.stringify({ a: { areaId: 'area-a', date: '2026-09-04' }, b: { areaId: 'area-b', date: '2026-09-04' } }),
      })
      assert(swapped.status === 200, 'swap → 200')
      const swappedBody = await swapped.json()
      assert(swappedBody.duties.length === 2, 'swap 回兩格')
      const after = await scheduleBody()
      assert(dutyAt(after, 'area-a', '2026-09-04') === staffB && dutyAt(after, 'area-b', '2026-09-04') === staffA, '兩格人員互換')

      // validate：結構與 summary
      const validated = await fetch(`${BASE}/schedules/2026-09/validate`, { method: 'POST' })
      assert(validated.status === 200, 'validate → 200')
      const validatedBody = await validated.json()
      assert(validatedBody.ok === false && validatedBody.summary.hard > 0, 'validate：有硬違規 → ok=false')

      // publish：先 409，帶 acknowledgeViolations 才成功
      const blocked = await fetch(`${BASE}/schedules/2026-09/publish`, {
        method: 'POST',
        headers: json,
        body: JSON.stringify({ acknowledgeViolations: false }),
      })
      assert(blocked.status === 409, '有硬違規發布 → 409')
      assert((await blocked.json()).error?.code === 'HARD_VIOLATIONS_PRESENT', '錯誤碼 HARD_VIOLATIONS_PRESENT')
      const published = await fetch(`${BASE}/schedules/2026-09/publish`, {
        method: 'POST',
        headers: json,
        body: JSON.stringify({ acknowledgeViolations: true }),
      })
      assert(published.status === 200, '帶 acknowledgeViolations 發布 → 200')
      const publishedBody = await published.json()
      assert(publishedBody.status === 'published', '發布後 status = published')
      assert(Array.isArray(publishedBody.carryOver) && publishedBody.carryOver.length > 0, '發布回月結轉清單')
      assert((await scheduleBody()).status === 'published', 'GET 值班表也是 published')

      // publishedVersion：草稿是 0、發布 +1、發布後改格不動、重新發布再 +1（revision 另算）
      assert(publishedBody.publishedVersion === 1, '第一次發布 publishedVersion = 1')
      assert((await scheduleBody()).publishedVersion === 1, 'GET 值班表 publishedVersion = 1')
      assert((await scheduleBody()).editedSincePublish === false, '剛發布 editedSincePublish = false')
      const reswap = await fetch(`${BASE}/schedules/2026-09/duties/swap`, {
        method: 'POST',
        headers: json,
        body: JSON.stringify({ a: { areaId: 'area-a', date: '2026-09-04' }, b: { areaId: 'area-b', date: '2026-09-04' } }),
      })
      assert(reswap.status === 200, '發布後 swap → 200')
      assert((await scheduleBody()).publishedVersion === 1, '發布後改格 publishedVersion 不變')
      assert((await scheduleBody()).editedSincePublish === true, '發布後改格 editedSincePublish = true（#75）')
      const republished = await fetch(`${BASE}/schedules/2026-09/publish`, {
        method: 'POST',
        headers: json,
        body: JSON.stringify({ acknowledgeViolations: true }),
      })
      assert(republished.status === 200, '重新發布 → 200')
      assert((await republished.json()).publishedVersion === 2, '重新發布 publishedVersion = 2')
      assert((await scheduleBody()).editedSincePublish === false, '重新發布後 editedSincePublish = false')

      // export：已發布也能匯出，位元組長度 > 0
      const exported = await fetch(`${BASE}/schedules/2026-09/export?layout=area-by-day`)
      assert(exported.status === 200, '匯出 → 200')
      const bytes = new Uint8Array(await exported.arrayBuffer())
      assert(bytes.length > 0, '匯出位元組長度 > 0')
    })

    // issue #81：0 人情境。放在最後，因為會把 mock store 換成空名冊。
    await mockOnly('16. 0 人情境（resetStore({ noStaff: true })）', '真後端無法重置成空名冊', async () => {
      resetStore({ noStaff: true })
      const empty = await (await fetch(`${BASE}/staff`)).json()
      assert(empty.items.length === 0, '空名冊 GET /staff items = []')
      assert(empty.counts.active === 0 && empty.counts.inactive === 0, '空名冊 counts.active = 0')
      const none = await fetch(`${BASE}/schedules/2026-09`)
      assert(none.status === 404, '空名冊沒有種子值班表 → 404')
      const first = await fetch(`${BASE}/staff`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ employeeNo: 'N001', name: '第一位', rankCode: 'PGY1' }),
      })
      assert(first.status === 201, '新增第一位人員 → 201')
      assert((await (await fetch(`${BASE}/staff`)).json()).counts.active === 1, '新增後 counts.active = 1')
    })
  } finally {
    server?.close()
  }

  console.log(`\n${passed} passed, ${failed} failed${skipped ? `, ${skipped} sections skipped` : ''}`)
  if (failed > 0) process.exitCode = 1
}

void main()
