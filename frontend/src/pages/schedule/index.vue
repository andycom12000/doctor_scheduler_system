<script setup lang="ts">
/**
 * SCREEN 01 排班主表：三個檢視分頁、點數看板、點格指派（issue #31），
 * 加上寫入流程（issue #34）：發布／重新發布、驗證約束、匯出、拖拉對調、已發布確認。
 * 列印（issue #32）：純 `@media print`，A4 橫式一頁一個月；印目前的格線檢視，單日詳表改印日 × 人。
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import PageLayout from '@/components/PageLayout.vue'
import { useYearMonth } from '@/composables/useYearMonth'
import { useResource, invalidate } from '@/composables/useResource'
import { useCalendar } from '@/composables/useCalendar'
import { exportSchedule, getSchedule, publishSchedule, swapDuties, validateSchedule } from '@/api/schedules'
import { getDayDetail, getPointBoard, listVacancies, listViolations } from '@/api/views'
import { getAreaSettings, getConstraints, getRankSettings } from '@/api/settings'
import { getBlockedDays } from '@/api/blockedDays'
import { listStaff } from '@/api/staff'
import { createSolverJob } from '@/api/solver'
import { ApiError } from '@/api/client'
import { describeError } from '@/api/errors'
import type { CellRef, Violation } from '@/api/types'
import { useConfirm } from '@/composables/useConfirm'
import { useToast } from '@/composables/useToast'
import { rememberJobId } from '../variants/jobStorage'
import { extractBusyJobId } from './lib/solverKickoff'
import EmptyState from './EmptyState.vue'
import AreaByDayGrid from './AreaByDayGrid.vue'
import DayByStaffGrid from './DayByStaffGrid.vue'
import DayDetailPanel from './DayDetailPanel.vue'
import PointBoardPanel from './PointBoardPanel.vue'
import UtilizationPanel from './UtilizationPanel.vue'
import ViolationSidebar from './ViolationSidebar.vue'
import CandidatePanel from './CandidatePanel.vue'
import { buildStaffDirectory, dutiesByArea, dutiesByStaff, toDayColumns, vacancyCountMap } from './lib/scheduleGrid'
import { buildCellRenderIndex, projectRenderIndexToAreaView, projectRenderIndexToStaffView } from './lib/violationStyle'
import { resolveJumpTarget } from './lib/cellNav'
import { downloadBlob, outputPromptKind, promptDraftOutput, type DraftOutputDecision } from './lib/draftOutput'
import { doubleBookingBlockMessage, printBlockMessage } from './lib/hardViolationBadge'
import { exportFileName, exportLayoutFor, isHardViolationsPresent, needsPublishedEditConfirm } from './lib/writeFlow'
import { isPrintShortcut, printHeading, printTabFor, type ScheduleTab } from './lib/printSheet'

const { ym } = useYearMonth()
const router = useRouter()

const year = computed(() => Number(ym.value.slice(0, 4)))
const calendar = useCalendar(year)
const days = computed(() => toDayColumns(calendar.daysOf(ym.value)))

// -- 值班表本體：404 是「值班表尚未產生」，不是錯誤（frontend-plan.md §3.1） -----------
const scheduleKey = computed(() => `schedules/${ym.value}`)
const schedule = useResource(scheduleKey, () => getSchedule(ym.value))

const isEmptyMonth = computed(() => schedule.error.value instanceof ApiError && schedule.error.value.status === 404)
const scheduleErrorMessage = computed(() =>
  schedule.error.value && !isEmptyMonth.value ? describeError(schedule.error.value) : null,
)

// -- 一般月份用它當區域中繼資料（areaTypeCode 分組），不必另外相信 Schedule.areas 的順序；
// 空狀態（issue #62）不需要，規格 §2 明文只准打 /staff 與 /calendars/{year}。
// 這兩支不能只看 `isEmptyMonth`——`isEmptyMonth` 要等 schedule 的 fetch 落地（成功或 404）
// 才有意義，但 `useResource` 的 `watch(..., { immediate: true })` 在 setup 當下就同步跑一次，
// 那時候 schedule 還在 loading、`isEmptyMonth` 必然還是 false，光看它一樣會在真正的空月份
// 誤打一次。改成也擋 `schedule.loading`：還不知道是不是空月份時先不打，schedule 落地後
// 才決定要不要打（是空月份就永遠不打，不是空月份就這時候才打，比原本「跟 schedule 平行打」
// 慢一點點，換來空月份真的一次都不打）。已經有資料時（寫入後的重抓）不算「還不知道」，
// 否則每次寫入後這兩支會短暫關掉、整個格線閃一下空白（issue #34）。
const scheduleSettling = computed(() => schedule.loading.value && !schedule.data.value)
const areaSettingsKey = computed(() => (scheduleSettling.value || isEmptyMonth.value ? null : 'settings/areas'))
const areaSettings = useResource(areaSettingsKey, () => getAreaSettings())

const constraintsKey = computed(() => (scheduleSettling.value || isEmptyMonth.value ? null : 'settings/constraints'))
const constraints = useResource(constraintsKey, () => getConstraints())
const fairOn = computed(
  () => (constraints.data.value?.soft.find((s) => s.code === 'S7_FAIRNESS_POINT')?.weight ?? 0) > 0,
)
// NP 的 quotaCap 是 null（不計額度）；點數看板改印 H6 的天數上限，不寫死 20 這個數字，
// 讀不到（例如 H6 被停用）就退回只顯示已值天數。
const npDutyCap = computed(() => {
  const h6 = constraints.data.value?.hard.find((h) => h.code === 'H6_NP_MONTHLY_DAYS')
  const cap = (h6?.params as { cap?: number } | undefined)?.cap
  return typeof cap === 'number' ? cap : null
})

// -- 人員名冊：獨立於值班表存在，全院約 34 人一次抓；用來補點數看板（只列在職）查不到的人
// ——最常見的是「當月有班、後來被停用」的人（PR #44 review：這種人的班不能憑空消失）。
// 空月份（issue #62）的日 × 人骨架也是靠這支 + rankSettingsRes 畫身分組色帶，不打 /staff 以外的東西 --
const staffListKey = ref('staff')
const staffListRes = useResource(staffListKey, () => listStaff())

const rankSettingsKey = ref('settings/ranks')
const rankSettingsRes = useResource(rankSettingsKey, () => getRankSettings())

// -- 不可排班日登記：獨立於值班表存在（ADR-0001），日 × 人檢視要疊這個狀態 --
const blockedDaysKey = computed(() => (schedule.data.value ? `blocked-days/${ym.value}` : null))
const blockedDaysRes = useResource(blockedDaysKey, () => getBlockedDays(ym.value))
const blockedSet = computed(
  () => new Set((blockedDaysRes.data.value?.entries ?? []).map((e) => `${e.staffId}|${e.date}`)),
)

// -- 由值班表推導的檢視：空月份一律不打（violationsKey 等在 schedule 不存在時是 null） --
const violationsKey = computed(() => (schedule.data.value ? `schedules/${ym.value}/violations` : null))
const violationsRes = useResource(violationsKey, () => listViolations(ym.value))

const pointBoardKey = computed(() => (schedule.data.value ? `schedules/${ym.value}/point-board` : null))
const pointBoardRes = useResource(pointBoardKey, () => getPointBoard(ym.value))

const vacanciesKey = computed(() => (schedule.data.value ? `schedules/${ym.value}/vacancies` : null))
const vacanciesRes = useResource(vacanciesKey, () => listVacancies(ym.value))

const pointBoardGroups = computed(() => pointBoardRes.data.value?.groups ?? [])
const staffDirectory = computed(() => buildStaffDirectory(pointBoardGroups.value, staffListRes.data.value?.items ?? []))
const dutyMapByArea = computed(() => dutiesByArea(schedule.data.value?.duties ?? []))
const dutyMapByStaff = computed(() => dutiesByStaff(schedule.data.value?.duties ?? []))
const cellRenderIndex = computed(() => buildCellRenderIndex(violationsRes.data.value?.violations ?? []))
// H1／H2／H5 這些「逐格」違規原始用 area:{areaId}:{date} 記位置；日 × 人是「人 × 日」的格線，
// 轉成「當天在那個區值班的人」才畫得出來（PR #44 review：H5 在日 × 人畫不出來）。
const dayByStaffRenderIndex = computed(() => projectRenderIndexToStaffView(cellRenderIndex.value, dutyMapByArea.value))
// 反方向：H3／H4／H6／H7 是 staff:{staffId}:{date}，區域 × 日要反查「當天值哪一區」才上得了色
// （PR #57 審查回饋 B1，右側欄限定區域 × 日之後這幾條違規原本完全不會被畫出來）。
const areaByDayRenderIndex = computed(() => projectRenderIndexToAreaView(cellRenderIndex.value, dutyMapByStaff.value))
const vacancyCounts = computed(() => vacancyCountMap(vacanciesRes.data.value?.byDate ?? []))

// -- 單日詳表用：`GET days/{date}` 只帶 area.code（如 CHIEF），中文全名與區域類型
// 中文名交給 `/settings/areas`／`schedule.areas` 查表，不在單日詳表元件裡另外寫死 --
const areaNameById = computed(() => new Map((schedule.data.value?.areas ?? []).map((a) => [a.id, a.name])))
const areaTypeNameByCode = computed(
  () => new Map((areaSettings.data.value?.areaTypes ?? []).map((t) => [t.code, t.name])),
)

// -- 標題列：副標「5 區 · N 人」，狀態（草稿／已發布 vN）由年月切換器的 badge 顯示 ----------------------------------
// 空狀態不重複顯示年月——左邊的 YearMonthSwitcher 本身已經是「2026 年 11 月班表」大字，
// PageLayout 副標再印一次「年月：2026-11」是純重複資訊（PR #63 審查回饋）。
const subtitle = computed(() => {
  const s = schedule.data.value
  if (isEmptyMonth.value) return ''
  if (!s) return `年月：${ym.value}`
  return `${s.areas.length} 區 · ${s.staffCount ?? 0} 人`
})
const isPublished = computed(() => schedule.data.value?.status === 'published')

/** 匯出、列印前的提示：草稿問先發布，發布後有修改問先重新發布（#75），已發布沒改過不問。 */
async function confirmOutput(label: string): Promise<DraftOutputDecision> {
  const s = schedule.data.value
  const kind = s ? outputPromptKind(s.status, s.editedSincePublish) : 'draft'
  if (kind === null) return 'direct'
  return promptDraftOutput(label, kind === 'edited' ? { editedFrom: s?.publishedVersion } : {})
}
const printTitle = computed(() => {
  const s = schedule.data.value
  return s ? printHeading(ym.value, s.status, s.publishedVersion, s.editedSincePublish) : ''
})
const publishLabel = computed(() => (isPublished.value ? '重新發布' : '發布'))

// -- 分頁 ---------------------------------------------------------------------------
const activeTab = ref<ScheduleTab>('area-by-day')

// -- 單日詳表：選取日期預設本月第一天，前一日／次一日在月內夾住 ----------------------
const selectedDate = ref<string | null>(null)
watch(ym, () => {
  selectedDate.value = null
})
const effectiveDate = computed(() => selectedDate.value ?? days.value[0]?.date ?? null)
const dayIndex = computed(() => days.value.findIndex((d) => d.date === effectiveDate.value))
const canPrevDay = computed(() => dayIndex.value > 0)
const canNextDay = computed(() => dayIndex.value >= 0 && dayIndex.value < days.value.length - 1)
function stepDay(delta: number): void {
  const next = days.value[dayIndex.value + delta]
  if (next) selectedDate.value = next.date
}

const dayDetailKey = computed(() =>
  schedule.data.value && effectiveDate.value ? `schedules/${ym.value}/days/${effectiveDate.value}` : null,
)
const dayDetailRes = useResource(dayDetailKey, () => getDayDetail(ym.value, effectiveDate.value as string))
const dayDetailErrorMessage = computed(() => (dayDetailRes.error.value ? describeError(dayDetailRes.error.value) : null))

// -- 寫入共用：快取失效、已發布確認、錯誤／結果訊息 --------------------------------------
// 失效整個 `schedules` 字首：值班表、違規、點數看板、空缺、單日詳表、候選人，
// 以及年月切換器讀的 `GET /schedules` 列表（狀態 tag 草稿 → 已發布）。
async function invalidateAfterWrite(): Promise<void> {
  await invalidate('schedules')
}

const { confirm } = useConfirm()

/** 已發布值班表：本次進入畫面後第一次修改要確認一次，確認過就不再問；換月份重置。 */
const publishedEditConfirmed = ref(false)
watch(ym, () => {
  publishedEditConfirmed.value = false
  toast.clear() // 換月份：上一個月的訊息不跟過來
})

async function confirmPublishedEdit(): Promise<boolean> {
  if (!needsPublishedEditConfirm(schedule.data.value?.status, publishedEditConfirmed.value)) return true
  const ok = await confirm({
    title: '修改已發布的值班表',
    message: '這份值班表已經發布。修改後不會自動重新發布，已匯出或列印的版本也不會跟著變。確定要修改嗎？',
    confirmText: '確定修改',
  })
  if (ok) publishedEditConfirmed.value = true
  return ok
}

// 訊息一律走共用 toast：一般訊息幾秒後自動消失，錯誤不自動消失（useToast.ts）。
const toast = useToast()

/**
 * 寫入進行中使用者可能已經切到別的月份：回應回來時只有「還在同一個月」才顯示訊息，
 * 否則會把 A 月的結果顯示在 B 月上。快取失效不受影響，照做。
 */
function isStale(month: string): boolean {
  return ym.value !== month
}

function fail(month: string, err: unknown): void {
  if (isStale(month)) return
  toast.error(describeError(err))
}

// -- 候選人面板 -------------------------------------------------------------------
const candidateTarget = ref<{ areaId: string; date: string } | null>(null)

const candidateAreaLabel = computed(() => {
  if (!candidateTarget.value) return ''
  const area = areaSettings.data.value?.areas.find((a) => a.id === candidateTarget.value?.areaId)
  return area?.name ?? candidateTarget.value.areaId
})
const candidateCurrentStaffId = computed(() => {
  if (!candidateTarget.value) return null
  return dutyMapByArea.value.get(`${candidateTarget.value.areaId}|${candidateTarget.value.date}`) ?? null
})

function openCell(areaId: string, date: string): void {
  candidateTarget.value = { areaId, date }
}

function closeCandidatePanel(): void {
  candidateTarget.value = null
}

async function onAssigned(): Promise<void> {
  await invalidateAfterWrite()
  candidateTarget.value = null
}

// -- 違規側欄「點一筆捲到該格」------------------------------------------------------
async function jumpToViolation(violation: Violation): Promise<void> {
  const cellKey = violation.cellKeys[0]
  if (!cellKey) return
  const target = resolveJumpTarget(cellKey, dutyMapByStaff.value)
  if (!target) return
  activeTab.value = target.tab
  await nextTick()
  if (target.domId) document.getElementById(target.domId)?.scrollIntoView({ block: 'center', inline: 'center' })
}

// -- 拖拉對調（區域 × 日）-------------------------------------------------------------
const swapping = ref(false)

async function onSwap(a: CellRef, b: CellRef): Promise<void> {
  if (swapping.value) return
  toast.clearErrors()
  const month = ym.value
  swapping.value = true
  try {
    if (!(await confirmPublishedEdit())) return
    await swapDuties(month, { a, b })
    await invalidateAfterWrite()
  } catch (err) {
    fail(month, err)
  } finally {
    swapping.value = false
  }
}

// -- 驗證約束：POST validate 之後刷新違規側欄。違規側欄只有一條資料路徑（GET violations 的快取），
// 寫入與驗證都用失效重抓餵它，跟點格指派一致，不另外把回應塞進狀態。
const validating = ref(false)

async function runValidate(): Promise<void> {
  if (validating.value) return
  toast.clearErrors()
  const month = ym.value
  validating.value = true
  try {
    const result = await validateSchedule(month)
    await invalidate(`schedules/${month}/violations`)
    if (isStale(month)) return
    const hard = result.summary.hard ?? 0
    const soft = result.summary.soft ?? 0
    toast.info(result.ok ? `驗證完成：沒有硬違規（軟違規 ${soft}）。` : `驗證完成：${hard} 項硬違規、${soft} 項軟違規。`)
  } catch (err) {
    fail(month, err)
  } finally {
    validating.value = false
  }
}

// -- 發布／重新發布：409 HARD_VIOLATIONS_PRESENT → 確認 → 帶 acknowledgeViolations 重發。
// 409 DOUBLE_BOOKING_PRESENT（同人同日兩區）不能確認略過，不跳確認，直接走 fail 的錯誤 toast（#68）-----
const publishing = ref(false)

/** 回傳是否發布成功（匯出「先發布再匯出」要接著用）。失敗與取消都是 false，訊息已經顯示。 */
async function runPublish(month = ym.value): Promise<boolean> {
  if (publishing.value) return false
  toast.clearErrors()
  publishing.value = true
  try {
    let result
    try {
      result = await publishSchedule(month)
    } catch (err) {
      if (!isHardViolationsPresent(err)) throw err
      const ok = await confirm({
        title: '仍有硬約束違規',
        message: '值班表還有硬約束違規。確定要在已知違規的情況下發布嗎？',
        confirmText: '仍要發布',
      })
      if (!ok) return false
      result = await publishSchedule(month, true)
    }
    // 發布途中換了月份：toast 帶月份，免得把 A 月的結果當成 B 月的。
    toast.info(isStale(month) ? `${month} 已發布 v${result.publishedVersion}` : `已發布 v${result.publishedVersion}`)
    await invalidateAfterWrite()
    return true
  } catch (err) {
    fail(month, err)
    return false
  } finally {
    publishing.value = false
  }
}

// -- 匯出 Excel：版面跟著目前檢視；草稿先問要不要發布。有 X1 直接擋；後端 409 DOUBLE_BOOKING_PRESENT 走 fail 的錯誤 toast ----------------------------------------
const exporting = ref(false)

async function downloadExport(month: string): Promise<void> {
  const { blob, filename } = await exportSchedule(month, exportLayoutFor(activeTab.value))
  downloadBlob(blob, exportFileName(filename, month))
}

async function runExport(): Promise<void> {
  if (exporting.value) return
  toast.clearErrors()
  const month = ym.value
  // 同人同日兩區：不跳草稿提示、不打 API，直接擋（後端匯出也會 409，#68）
  const blocked = doubleBookingBlockMessage(violationsRes.data.value?.violations ?? [], '匯出')
  if (blocked) {
    toast.error(blocked)
    return
  }
  exporting.value = true
  try {
    const decision = await confirmOutput('匯出')
    if (decision === 'cancel') return
    if (decision === 'publish-first' && !(await runPublish(month))) return
    await downloadExport(month)
  } catch (err) {
    fail(month, err)
  } finally {
    exporting.value = false
  }
}

// -- 列印：有 X1 或確認不了違規清單就擋（前端列印前重抓，見 printBlockMessage）。版面是各格線元件與
// styles.css 的 `@media print`（#32）；這裡決定印哪個檢視，單日詳表沒有列印版面，先切到日 × 人再印 ------------
// Ctrl+P 不受模態對話框擋，草稿提示還開著時再按一次不能再進來一輪。
const printing = ref(false)

async function runPrint(): Promise<void> {
  if (printing.value) return
  printing.value = true
  try {
    await printFlow()
  } finally {
    printing.value = false
  }
}

async function printFlow(): Promise<void> {
  toast.clearErrors()
  const month = ym.value
  // 列印沒有後端守門：先重新抓一次違規清單再判斷（fail-closed，抓不到就不印）。
  // reload 不丟例外，失敗會留在 error；快取的舊清單不能當成「確認過」。
  await violationsRes.reload()
  if (isStale(month)) return
  const blocked = printBlockMessage(violationsRes.data.value?.violations ?? null, violationsRes.error.value !== null)
  if (blocked) {
    toast.error(blocked)
    return
  }
  const decision = await confirmOutput('列印')
  if (decision === 'cancel') return
  if (decision === 'publish-first' && !(await runPublish(month))) return
  // 等待期間換了月份，畫面已不是使用者要印的那個月。
  if (isStale(month)) return
  // 格線是 v-if，切了分頁要等 DOM 換好才印。切過去就留在那個分頁，使用者看得到印的是什麼。
  activeTab.value = printTabFor(activeTab.value)
  await nextTick()
  if (isStale(month)) return
  window.print()
}

// Ctrl+P 攔下來改走 runPrint（擋 X1、草稿提示）；右鍵選單的列印由 Shell 拿掉。沒有班表、寫入中、
// 或候選人面板開著（按鈕被面板遮罩蓋住，面板裡的指派可能正在送出）就只吞掉，不印。
function onKeyDown(e: KeyboardEvent): void {
  if (!isPrintShortcut(e)) return
  e.preventDefault()
  if (e.repeat || !schedule.data.value || writeBusy.value || candidateTarget.value) return
  void runPrint()
}

// 仍有漏網的列印（例如開發期瀏覽器的選單）：至少把單日詳表切成日 × 人，才不會印出沒有列印版面的畫面。
// Vue 的 DOM 更新是 microtask，在事件處理結束、瀏覽器排版列印之前就會完成。
function onBeforePrint(): void {
  if (schedule.data.value) activeTab.value = printTabFor(activeTab.value)
}
onMounted(() => {
  window.addEventListener('keydown', onKeyDown)
  window.addEventListener('beforeprint', onBeforePrint)
})
onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeyDown)
  window.removeEventListener('beforeprint', onBeforePrint)
})

// 列印也算：重抓違規清單、確認可以印的這段期間不能拖拉對調，免得檢查完又冒出同人同日兩區。
const writeBusy = computed(
  () => swapping.value || validating.value || publishing.value || exporting.value || printing.value,
)

// -- 工具列：重新求解導去變體頁；其餘寫入流程見上 ------------------------------------------
// 任務指示與空月份的「求解」連結都導向 /variants/{ym}；issue 內文另寫「SCREEN 05」，
// 兩者不一致時取任務指示與空月份連結一致的那個（PR 說明有記錄）。
function goToVariants(): void {
  router.push({ name: 'variants', params: { ym: ym.value } })
}

// -- 空狀態「開始求解」（issue #62）：建工作 → rememberJobId → 導去變體頁接手進度覆蓋層。
// 只有真的建成才記 jobId／導頁；成功與 409 SOLVER_BUSY 兩條路徑都把 jobId 一併帶進
// query（不只寫 localStorage）——`rememberJobId` 寫入失敗時（例如 WebView2 的使用者
// 資料目錄還沒就緒，見 jobStorage.ts 檔頭說明），變體頁的 `attachJob` 仍能從
// `route.query.job` 接手，不會退回「這個月還沒有求解紀錄」。`attachJob` 會驗證
// yearMonth 是否吻合，不吻合就照它自己的規則退回。
const solving = ref(false)
const solveError = ref<string | null>(null)

async function startSolve(): Promise<void> {
  if (solving.value) return
  solving.value = true
  solveError.value = null
  try {
    const job = await createSolverJob({ yearMonth: ym.value, variantCount: 3, timeLimitSecPerVariant: 15 })
    rememberJobId(ym.value, job.jobId)
    await router.push({ name: 'variants', params: { ym: ym.value }, query: { job: job.jobId } })
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      const busyJobId = extractBusyJobId(err.body)
      await router.push({
        name: 'variants',
        params: { ym: ym.value },
        query: busyJobId ? { job: busyJobId } : {},
      })
      return
    }
    solveError.value = describeError(err)
  } finally {
    solving.value = false
  }
}

// -- 空狀態的兩支必要資料（staff／calendar／ranks）沒載完或失敗，不能拿去畫矩陣（規格 §2、§7）--
const emptyStateBlockingError = computed(() => {
  if (isEmptyMonth.value && staffListRes.error.value) return describeError(staffListRes.error.value)
  if (isEmptyMonth.value && calendar.error.value) return describeError(calendar.error.value)
  if (isEmptyMonth.value && rankSettingsRes.error.value) return describeError(rankSettingsRes.error.value)
  return null
})
const emptyStateLoading = computed(
  () =>
    isEmptyMonth.value &&
    !emptyStateBlockingError.value &&
    ((staffListRes.loading.value && !staffListRes.data.value) ||
      (calendar.loading.value && !calendar.calendar.value) ||
      (rankSettingsRes.loading.value && !rankSettingsRes.data.value)),
)
</script>

<template>
  <PageLayout title="排班主表" :subtitle="subtitle" :fill="isEmptyMonth">
    <template #actions>
      <nav
        v-if="!isEmptyMonth && !scheduleErrorMessage && schedule.data.value"
        class="schedule__tabs"
        role="tablist"
      >
        <button
          type="button"
          role="tab"
          class="schedule__tab"
          :class="{ 'schedule__tab--active': activeTab === 'area-by-day' }"
          :aria-selected="activeTab === 'area-by-day'"
          @click="activeTab = 'area-by-day'"
        >
          區域 × 日
        </button>
        <button
          type="button"
          role="tab"
          class="schedule__tab"
          :class="{ 'schedule__tab--active': activeTab === 'day-by-staff' }"
          :aria-selected="activeTab === 'day-by-staff'"
          @click="activeTab = 'day-by-staff'"
        >
          日 × 人
        </button>
        <button
          type="button"
          role="tab"
          class="schedule__tab"
          :class="{ 'schedule__tab--active': activeTab === 'day-detail' }"
          :aria-selected="activeTab === 'day-detail'"
          @click="activeTab = 'day-detail'"
        >
          單日詳表
        </button>
      </nav>
      <!-- 空狀態（issue #62）：匯出／驗證／重新求解／發布這四顆需要既有班表，一律不顯示
           （規格 §3）。「尚無班表」的狀態標籤改由 YearMonthSwitcher 自己的 .tag.tag-outline
           負責（issue #62 段落 B），這裡不重複畫一顆。 -->
      <template v-if="!isEmptyMonth">
        <button type="button" class="btn btn-secondary" :disabled="writeBusy" @click="runExport">匯出 Excel</button>
        <button type="button" class="btn btn-secondary" :disabled="writeBusy" @click="runPrint">列印</button>
        <button type="button" class="btn btn-secondary" :disabled="writeBusy" @click="runValidate">驗證約束</button>
        <button type="button" class="btn btn-secondary" @click="goToVariants">重新求解</button>
        <button type="button" class="btn btn-primary" :disabled="writeBusy" @click="runPublish()">
          {{ publishLabel }}
        </button>
      </template>
    </template>

    <div v-if="schedule.loading.value && !schedule.data.value && !isEmptyMonth" class="schedule-state">載入中…</div>
    <div v-else-if="scheduleErrorMessage" class="schedule-state">{{ scheduleErrorMessage }}</div>
    <div v-else-if="isEmptyMonth && emptyStateBlockingError" class="schedule-state">{{ emptyStateBlockingError }}</div>
    <div v-else-if="isEmptyMonth && emptyStateLoading" class="schedule-state">載入中…</div>

    <EmptyState
      v-else-if="isEmptyMonth"
      :ym="ym"
      :staff="staffListRes.data.value?.items ?? []"
      :ranks="rankSettingsRes.data.value?.ranks ?? []"
      :groups="rankSettingsRes.data.value?.groups ?? []"
      :days="days"
      :solving="solving"
      :solve-error="solveError"
      @solve="startSolve"
    />

    <div v-else class="schedule">
      <h1 class="print-only schedule__print-title">{{ printTitle }}</h1>
      <div class="schedule__content">
        <div class="schedule__main">
          <AreaByDayGrid
            v-if="activeTab === 'area-by-day'"
            :area-types="areaSettings.data.value?.areaTypes ?? []"
            :areas="schedule.data.value?.areas ?? []"
            :days="days"
            :duty-map="dutyMapByArea"
            :staff-directory="staffDirectory"
            :cell-render-index="areaByDayRenderIndex"
            :vacancy-counts="vacancyCounts"
            :point-board-groups="pointBoardGroups"
            :swap-disabled="writeBusy"
            @cell-click="openCell"
            @swap="onSwap"
          />
          <DayByStaffGrid
            v-else-if="activeTab === 'day-by-staff'"
            :areas="schedule.data.value?.areas ?? []"
            :days="days"
            :point-board-groups="pointBoardGroups"
            :duties-by-staff="dutyMapByStaff"
            :staff-render-index="dayByStaffRenderIndex"
            :vacancy-counts="vacancyCounts"
            :staff-directory="staffDirectory"
            :blocked-set="blockedSet"
            @cell-click="openCell"
          />
          <DayDetailPanel
            v-else
            :date="effectiveDate ?? ym"
            :detail="dayDetailRes.data.value"
            :loading="dayDetailRes.loading.value"
            :error-message="dayDetailErrorMessage"
            :can-prev="canPrevDay"
            :can-next="canNextDay"
            :area-names="areaNameById"
            :area-type-names="areaTypeNameByCode"
            @prev="stepDay(-1)"
            @next="stepDay(1)"
            @cell-click="openCell"
          />

          <PointBoardPanel
            v-if="pointBoardGroups.length > 0"
            class="screen-only"
            :groups="pointBoardGroups"
            :fair-on="fairOn"
            :published="schedule.data.value?.status === 'published'"
            :np-duty-cap="npDutyCap"
          />
        </div>

        <aside v-if="activeTab === 'area-by-day'" class="schedule__aside screen-only">
          <UtilizationPanel :groups="pointBoardGroups" />
          <ViolationSidebar :violations="violationsRes.data.value?.violations ?? []" @jump="jumpToViolation" />
        </aside>
      </div>
    </div>

    <CandidatePanel
      v-if="candidateTarget"
      class="screen-only"
      :ym="ym"
      :area-id="candidateTarget.areaId"
      :area-label="candidateAreaLabel"
      :date="candidateTarget.date"
      :current-staff-id="candidateCurrentStaffId"
      :before-write="confirmPublishedEdit"
      @close="closeCandidatePanel"
      @assigned="onAssigned"
    />
  </PageLayout>
</template>

<style scoped>
.schedule-state {
  padding: var(--space-4);
  font-size: 13px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.schedule {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  height: 100%;
  min-height: 0;
}

/* 分頁列移進工具列（PageLayout 的 actions slot）：#53 設計稿把它跟匯出／驗證／求解／發布
   放在同一列、分頁在左、其餘按鈕在右。`page-layout__actions` 是內容自撐開的 flex row（不能改
   PageLayout.vue），這裡用固定 margin-right 隔開兩組，不用 flex:1 撐開（撐不開，見 PR 說明）。 */
.schedule__tabs {
  display: flex;
  gap: 2px;
  flex: none;
  margin-right: var(--space-6);
}

.schedule__tab {
  padding: 6px 14px;
  font: 500 13px var(--font-heading);
  background: transparent;
  border: 1px solid var(--color-divider);
  cursor: pointer;
  color: color-mix(in srgb, var(--color-text) 60%, transparent);
}

.schedule__tab--active {
  color: var(--color-accent);
  border-color: var(--color-accent);
  background: var(--color-accent-100);
}

.schedule__content {
  display: flex;
  gap: var(--space-4);
  flex: 1;
  min-height: 0;
}

.schedule__main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  overflow: auto;
}

.schedule__aside {
  width: 306px;
  flex: none;
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding-left: var(--space-4);
  border-left: 1px solid var(--color-divider);
  overflow-y: auto;
}

.schedule__print-title {
  margin: 0 0 4px;
  font-size: 15px;
}

/* 列印（#32）：解掉整條捲動容器，格線才不會被裁成一個視窗高 */
@media print {
  .schedule,
  .schedule__content {
    display: block;
    height: auto;
  }

  .schedule__main {
    overflow: visible;
    gap: 0;
  }
}
</style>
