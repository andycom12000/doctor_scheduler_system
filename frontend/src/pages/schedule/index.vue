<script setup lang="ts">
/**
 * SCREEN 01 排班主表原型（issue #31）：三個檢視分頁、點數看板、點格指派。
 * 只做原型範圍——發布、匯出、拖拉對調、已發布確認在 #34。
 */
import { computed, nextTick, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import PageLayout from '@/components/PageLayout.vue'
import { useYearMonth } from '@/composables/useYearMonth'
import { useResource, invalidate } from '@/composables/useResource'
import { useCalendar } from '@/composables/useCalendar'
import { getSchedule, setDuty } from '@/api/schedules'
import { getDayDetail, getPointBoard, listVacancies, listViolations } from '@/api/views'
import { getAreaSettings, getConstraints } from '@/api/settings'
import { getBlockedDays } from '@/api/blockedDays'
import { listStaff } from '@/api/staff'
import { ApiError } from '@/api/client'
import { describeError } from '@/api/errors'
import type { Violation } from '@/api/types'
import EmptyState from './EmptyState.vue'
import AreaByDayGrid from './AreaByDayGrid.vue'
import DayByStaffGrid from './DayByStaffGrid.vue'
import DayDetailPanel from './DayDetailPanel.vue'
import PointBoardPanel from './PointBoardPanel.vue'
import UtilizationPanel from './UtilizationPanel.vue'
import ViolationSidebar from './ViolationSidebar.vue'
import CandidatePanel from './CandidatePanel.vue'
import { buildStaffDirectory, dutiesByArea, dutiesByStaff, toDayColumns, vacancyCountMap } from './lib/scheduleGrid'
import { buildCellRenderIndex, projectRenderIndexToStaffView } from './lib/violationStyle'
import { domIdForCellKey, tabForCellKey } from './lib/cellNav'

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

// -- 空月份畫格線用；一般月份也拿它當區域中繼資料（areaTypeCode 分組），不必另外相信 Schedule.areas 的順序 --
const areaSettingsKey = ref('settings/areas')
const areaSettings = useResource(areaSettingsKey, () => getAreaSettings())

const constraintsKey = ref('settings/constraints')
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
// ——最常見的是「當月有班、後來被停用」的人（PR #44 review：這種人的班不能憑空消失）--
const staffListKey = ref('staff')
const staffListRes = useResource(staffListKey, () => listStaff())

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
const vacancyCounts = computed(() => vacancyCountMap(vacanciesRes.data.value?.byDate ?? []))

// -- 單日詳表用：`GET days/{date}` 只帶 area.code（如 CHIEF），中文全名與區域類型
// 中文名交給 `/settings/areas`／`schedule.areas` 查表，不在單日詳表元件裡另外寫死 --
const areaNameById = computed(() => new Map((schedule.data.value?.areas ?? []).map((a) => [a.id, a.name])))
const areaTypeNameByCode = computed(
  () => new Map((areaSettings.data.value?.areaTypes ?? []).map((t) => [t.code, t.name])),
)

// -- 標題列：「5 區 · N 人 · 草稿 vN」 -----------------------------------------------
const subtitle = computed(() => {
  const s = schedule.data.value
  if (!s) return `年月：${ym.value}`
  const statusLabel = s.status === 'published' ? '已發布' : '草稿'
  return `${s.areas.length} 區 · ${s.staffCount ?? 0} 人 · ${statusLabel} v${s.revision}`
})
const publishLabel = computed(() => (schedule.data.value?.status === 'published' ? '重新發布' : '發布'))

// -- 分頁 ---------------------------------------------------------------------------
type ScheduleTab = 'area-by-day' | 'day-by-staff' | 'day-detail'
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

// -- 候選人面板 + 空月份的「先建空草稿」------------------------------------------
const candidateTarget = ref<{ areaId: string; date: string } | null>(null)
const priming = ref(false)
const primeError = ref<string | null>(null)

const candidateAreaLabel = computed(() => {
  if (!candidateTarget.value) return ''
  const area = areaSettings.data.value?.areas.find((a) => a.id === candidateTarget.value?.areaId)
  return area?.name ?? candidateTarget.value.areaId
})
const candidateCurrentStaffId = computed(() => {
  if (!candidateTarget.value) return null
  return dutyMapByArea.value.get(`${candidateTarget.value.areaId}|${candidateTarget.value.date}`) ?? null
})

/**
 * 空月份點格：先用 `PATCH duties`（`staffId: null`）讓後端自動建一份空草稿，
 * 再開候選人面板——`GET candidates` 跟點數看板／違規一樣，該月尚無值班表時回 404
 * （api-contract.yaml `listViolations` 的說明「點數看板、空缺、候選人同此」），
 * 這裡的允許端點清單也沒有 `/staff`，priming 是唯一能在空月份湊出候選人清單的方法。
 *
 * `invalidate('schedules')`（不是 `schedules/${ym}`）：這是唯一一次會把某個月從
 * 「不存在」變成「草稿」的寫入，要連 `schedules` 這個清單 key 一起打掉，
 * 頂列年月切換器的狀態標籤才會跟著更新。
 */
async function openCell(areaId: string, date: string): Promise<void> {
  if (!isEmptyMonth.value) {
    candidateTarget.value = { areaId, date }
    return
  }
  if (priming.value) return
  priming.value = true
  primeError.value = null
  try {
    await setDuty(ym.value, { areaId, date, staffId: null })
    await invalidate('schedules')
    candidateTarget.value = { areaId, date }
  } catch (err) {
    primeError.value = describeError(err)
  } finally {
    priming.value = false
  }
}

function closeCandidatePanel(): void {
  candidateTarget.value = null
}

async function onAssigned(): Promise<void> {
  await invalidate(`schedules/${ym.value}`)
  candidateTarget.value = null
}

// -- 違規側欄「點一筆捲到該格」------------------------------------------------------
async function jumpToViolation(violation: Violation): Promise<void> {
  const cellKey = violation.cellKeys[0]
  if (!cellKey) return
  const tab = tabForCellKey(cellKey)
  if (tab) activeTab.value = tab
  await nextTick()
  const domId = domIdForCellKey(cellKey)
  if (domId) document.getElementById(domId)?.scrollIntoView({ block: 'center', inline: 'center' })
}

// -- 工具列：只有「重新求解」有作用；其餘停用等 #34 -----------------------------------
// 任務指示與空月份的「求解」連結都導向 /variants/{ym}；issue 內文另寫「SCREEN 05」，
// 兩者不一致時取任務指示與空月份連結一致的那個（PR 說明有記錄）。
function goToVariants(): void {
  router.push({ name: 'variants', params: { ym: ym.value } })
}
function goToBlockedDays(): void {
  router.push({ name: 'blockedDays', params: { ym: ym.value } })
}
</script>

<template>
  <PageLayout title="排班主表" :subtitle="subtitle">
    <template #actions>
      <button type="button" class="btn btn-secondary" disabled title="#34">匯出 Excel</button>
      <button type="button" class="btn btn-secondary" disabled title="#34">驗證約束</button>
      <button type="button" class="btn btn-secondary" @click="goToVariants">重新求解</button>
      <button type="button" class="btn btn-primary" disabled title="#34">{{ publishLabel }}</button>
    </template>

    <div v-if="schedule.loading.value && !schedule.data.value && !isEmptyMonth" class="schedule-state">載入中…</div>
    <div v-else-if="scheduleErrorMessage" class="schedule-state">{{ scheduleErrorMessage }}</div>

    <EmptyState
      v-else-if="isEmptyMonth"
      :areas="areaSettings.data.value?.areas ?? []"
      :days="days"
      :ym="ym"
      :priming="priming"
      :prime-error="primeError"
      @cell-click="openCell"
      @register-blocked-days="goToBlockedDays"
      @solve="goToVariants"
    />

    <div v-else class="schedule">
      <nav class="schedule__tabs">
        <button
          type="button"
          class="schedule__tab"
          :class="{ 'schedule__tab--active': activeTab === 'area-by-day' }"
          @click="activeTab = 'area-by-day'"
        >
          區域 × 日
        </button>
        <button
          type="button"
          class="schedule__tab"
          :class="{ 'schedule__tab--active': activeTab === 'day-by-staff' }"
          @click="activeTab = 'day-by-staff'"
        >
          日 × 人
        </button>
        <button
          type="button"
          class="schedule__tab"
          :class="{ 'schedule__tab--active': activeTab === 'day-detail' }"
          @click="activeTab = 'day-detail'"
        >
          單日詳表
        </button>
      </nav>

      <div class="schedule__content">
        <div class="schedule__main">
          <AreaByDayGrid
            v-if="activeTab === 'area-by-day'"
            :area-types="areaSettings.data.value?.areaTypes ?? []"
            :areas="schedule.data.value?.areas ?? []"
            :days="days"
            :duty-map="dutyMapByArea"
            :staff-directory="staffDirectory"
            :cell-render-index="cellRenderIndex"
            :vacancy-counts="vacancyCounts"
            :point-board-groups="pointBoardGroups"
            @cell-click="openCell"
          />
          <DayByStaffGrid
            v-else-if="activeTab === 'day-by-staff'"
            :areas="schedule.data.value?.areas ?? []"
            :days="days"
            :point-board-groups="pointBoardGroups"
            :duties-by-staff="dutyMapByStaff"
            :staff-render-index="dayByStaffRenderIndex"
            :vacancy-by-date="vacanciesRes.data.value?.byDate ?? []"
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
            :groups="pointBoardGroups"
            :fair-on="fairOn"
            :published="schedule.data.value?.status === 'published'"
            :np-duty-cap="npDutyCap"
          />
        </div>

        <aside class="schedule__aside">
          <UtilizationPanel :groups="pointBoardGroups" />
          <ViolationSidebar :violations="violationsRes.data.value?.violations ?? []" @jump="jumpToViolation" />
        </aside>
      </div>
    </div>

    <CandidatePanel
      v-if="candidateTarget"
      :ym="ym"
      :area-id="candidateTarget.areaId"
      :area-label="candidateAreaLabel"
      :date="candidateTarget.date"
      :current-staff-id="candidateCurrentStaffId"
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

.schedule__tabs {
  display: flex;
  gap: 2px;
  flex: none;
}

.schedule__tab {
  padding: 6px 14px;
  font: 500 13px var(--font-heading);
  background: transparent;
  border: 1px solid var(--color-divider);
  border-bottom: none;
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
</style>
