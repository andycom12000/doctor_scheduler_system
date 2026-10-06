/**
 * 主線劇本（issue #84）：全新資料庫、空名冊，照使用者的操作順序從畫面走到發布、匯出。
 * 後端對應的是 tests/Scheduler.Api.Tests/EndToEndFromZeroTests.cs（#83）；這裡驗的是它測不到的畫面層：
 * 空狀態引導、表單、筆刷點格、求解進度與變體套用、硬違規確認對話框、狀態 badge、下載請求。
 *
 * 月份用 2026-02（2026 才有內建國定假日，且在過去、不依賴「今天」），跟 #83 一致。
 * 名冊的人數組成照 DefaultRanks.ReferenceHeadcount（34 人，跟 mocks 的 staff.ts 同一份）；
 * 大部分用 API 建（畫面逐一建 34 人太慢），畫面上只新增 1 人驗證表單。
 */
import { expect, test, type APIRequestContext } from '@playwright/test'

const MONTH = '2026-02'

/** 身分 → 人數。UI 建 1 位 R1，API 建其餘（R1 少 1 人），總數 34。 */
const HEADCOUNT: Record<string, number> = {
  PGY1: 2,
  PGY2: 4,
  R1: 4,
  R2: 3,
  R3: 4,
  R4: 4,
  R5: 5,
  R6: 4,
  PTR: 3,
  NP: 1,
}

const UI_PERSON = { name: '畫面新增醫師', employeeNo: 'E2E-UI', rankCode: 'R1' }

interface StaffDto {
  id: string
  name: string
  employeeNo: string
  rankCode: string
}

async function createViaApi(request: APIRequestContext): Promise<StaffDto[]> {
  const created: StaffDto[] = []
  let n = 0
  for (const [rankCode, count] of Object.entries(HEADCOUNT)) {
    const apiCount = rankCode === UI_PERSON.rankCode ? count - 1 : count
    for (let i = 0; i < apiCount; i++) {
      n += 1
      const body = { employeeNo: `E2E-${String(n).padStart(3, '0')}`, name: `測試醫師${String(n).padStart(2, '0')}`, rankCode }
      const res = await request.post('/api/staff', { data: body })
      expect(res.status(), `建立 ${body.name}`).toBe(201)
      created.push((await res.json()) as StaffDto)
    }
  }
  return created
}

test('全新資料庫：建人員 → 登記不可排班日 → 求解套用 → 發布 → 匯出', async ({ page, request }) => {
  // 求解送出的請求壓短：畫面按鈕寫死 3 份 × 15 秒，這裡改成 2 份 × 8 秒（其餘流程照畫面走）
  await page.route('**/api/solver-jobs', async (route) => {
    if (route.request().method() !== 'POST') return route.fallback()
    const body = route.request().postDataJSON() as Record<string, unknown>
    // 契約改名時要大聲失敗，不能默默變回 3 份 × 15 秒
    expect(Object.keys(body)).toEqual(expect.arrayContaining(['variantCount', 'timeLimitSecPerVariant']))
    await route.continue({ postData: JSON.stringify({ ...body, variantCount: 2, timeLimitSecPerVariant: 8 }) })
  })

  // 1. 0 人：排班主表顯示空狀態引導 → 進人員維護
  await page.goto(`/schedules/${MONTH}`)
  const guide = page.getByTestId('no-staff-guide')
  await expect(guide).toBeVisible()
  await expect(guide).toContainText('尚未建立人員')
  await guide.getByRole('link', { name: '前往人員維護' }).click()
  await expect(page).toHaveURL(/\/staff$/)

  // 2. 畫面新增 1 人，其餘用 API 建，重新載入後名冊共 34 人
  await page.getByRole('button', { name: /新增人員/ }).click()
  await page.getByRole('textbox', { name: '姓名', exact: true }).fill(UI_PERSON.name)
  await page.getByRole('textbox', { name: '員編', exact: true }).fill(UI_PERSON.employeeNo)
  await page.locator('label.field').filter({ has: page.getByText('身分', { exact: true }) }).locator('select').selectOption(UI_PERSON.rankCode)
  await page.getByRole('button', { name: '儲存變更' }).click()
  await expect(page.getByRole('button', { name: new RegExp(UI_PERSON.name) })).toBeVisible()

  const apiStaff = await createViaApi(request)
  await page.reload()
  await expect(page.getByRole('button', { name: new RegExp(UI_PERSON.name) })).toBeVisible()
  const all = (await (await request.get('/api/staff')).json()) as { items: StaffDto[] }
  expect(all.items).toHaveLength(34)
  const uiPerson = all.items.find((s) => s.employeeNo === UI_PERSON.employeeNo)!

  // 3. 不可排班日：API 登記 5 位醫師各 2 天（日期與 #83 同，皆為 2026-02 的平日），畫面上點一格
  const byRank = (rank: string) => apiStaff.find((s) => s.rankCode === rank)!
  const plan: Array<[string, string[]]> = [
    ['R4', ['02', '03']],
    ['R5', ['04', '05']],
    ['R2', ['06', '09']],
    ['R3', ['10', '11']],
    ['PGY2', ['23', '24']],
  ]
  for (const [rank, days] of plan) {
    for (const day of days) {
      const res = await request.put(`/api/blocked-days/${MONTH}/${byRank(rank).id}/${MONTH}-${day}`)
      expect(res.status()).toBe(200)
    }
  }
  await page.goto(`/blocked-days/${MONTH}`)
  const cell = page.locator(`[data-paint-key="${uiPerson.id}|${MONTH}-25"]`)
  await expect(cell).toBeVisible()
  await cell.click()
  await expect(cell).toHaveText('×')

  // 4. 帶入求解（確認對話框）→ 變體頁等求解完成 → 選定其中一份 → 回到排班主表
  await page.getByRole('button', { name: '帶入求解' }).click()
  await page.getByRole('dialog').getByRole('button', { name: '開始求解' }).click()
  await expect(page).toHaveURL(new RegExp(`/variants/${MONTH}\\?job=`))
  const pick = page.getByRole('button', { name: '選定此變體' }).first()
  await expect(pick).toBeVisible({ timeout: 90_000 })
  // 壓短後的請求確實生效：2 份變體
  await expect(page.getByRole('button', { name: '選定此變體' })).toHaveCount(2)
  await expect(pick).toBeEnabled()
  await pick.click()
  await expect(page).toHaveURL(new RegExp(`/schedules/${MONTH}$`))

  // 5. 排班主表：草稿、看得到值班。為了讓「硬違規確認」一定出現（求解結果可能零違規），
  // 用 API 清掉一格（造一個空缺，同 #83），重新載入後違規側欄必有硬違規。
  // 狀態 badge 不經 reload 就要是「草稿」（#105：套用變體後清單 key 也要失效）。
  await expect(page.locator('.status-badge')).toContainText('草稿')
  const schedule = (await (await request.get(`/api/schedules/${MONTH}`)).json()) as {
    duties: Array<{ areaId: string; date: string; staffId: string | null }>
  }
  const assigned = schedule.duties.filter((d) => d.staffId !== null)
  expect(assigned.length).toBeGreaterThan(0)
  await expect(page.getByRole('button', { name: /測試醫師|畫面新增醫師/ }).first()).toBeVisible()
  const target = assigned[0]
  const cleared = await request.patch(`/api/schedules/${MONTH}/duties`, {
    data: { areaId: target.areaId, date: target.date, staffId: null },
  })
  expect(cleared.status()).toBe(200)
  await page.reload()
  const sidebar = page.locator('.violation-sidebar')
  await expect(sidebar).toBeVisible()
  await expect(sidebar.locator('.violation-sidebar__row').first()).toBeVisible()
  await expect(sidebar.locator('.violation-sidebar__badge--hard').first()).toBeVisible()

  // 6. 發布：硬違規 → 確認對話框 → 仍要發布 → 狀態變成已發布 v1
  await page.getByRole('button', { name: '發布', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('仍有硬約束違規')
  await dialog.getByRole('button', { name: '仍要發布' }).click()
  await expect(page.locator('.status-badge')).toContainText('已發布 v1')

  // 7. 匯出：API 回 200（存檔對話框是 Shell 的事，不測）
  const exported = page.waitForResponse((r) => r.url().includes(`/api/schedules/${MONTH}/export`))
  await page.getByRole('button', { name: '匯出 Excel' }).click()
  const exportResponse = await exported
  expect(exportResponse.status()).toBe(200)
  expect(exportResponse.headers()['content-type']).toContain('spreadsheetml.sheet')
})
