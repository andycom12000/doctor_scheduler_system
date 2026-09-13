<script setup lang="ts">
/**
 * SCREEN 05 不可排班日登記（issue #29）。獨立於值班表存在（ADR-0001）：矩陣寫入直接打
 * `/blocked-days/{ym}`，不經過 `/schedules/{ym}`；「值班表尚未產生」只是標題副標的一個狀態。
 *
 * 寫入策略：每次 PUT／DELETE 用回應的 `BlockedDayMutationResult` 就地patch本地已載入的
 * `BlockedDayRegistration`（`applyBlockedDayMutation`），不整份重抓——拖曳塗一整排時
 * 不必等每一格的 GET 才更新畫面。等一次拖曳（或一次點擊）結束、且這批寫入全部落地
 * （`pendingWrites`）才呼叫 `invalidate('blocked-days/{ym}')`，讓可行性預警
 * （`blocked-days/{ym}/feasibility` 這個子資源 key 就放在這個字首下）與登記表本身重新對一次帳。
 */
import { computed, onBeforeUnmount, ref } from 'vue'
import { useRouter } from 'vue-router'
import PageLayout from '@/components/PageLayout.vue'
import { useYearMonth } from '@/composables/useYearMonth'
import { useCalendar } from '@/composables/useCalendar'
import { useConfirm } from '@/composables/useConfirm'
import { invalidate, useResource } from '@/composables/useResource'
import { ApiError } from '@/api/client'
import { describeError } from '@/api/errors'
import { clearBlockedDay, getBlockedDays, getFeasibility, setBlockedDay } from '@/api/blockedDays'
import { getSchedule } from '@/api/schedules'
import { createSolverJob } from '@/api/solver'
import { getAreaSettings, getEligibilityMatrix, getRankSettings } from '@/api/settings'
import { listStaff } from '@/api/staff'
import BlockedDayMatrix from './BlockedDayMatrix.vue'
import BlockedDaySidebar from './BlockedDaySidebar.vue'
import {
  buildGroupViews,
  buildShortageDays,
  chiefAreaTypeCode,
  chiefAvailabilityByDate,
  dateCountMap,
  evaluateWardSqueezeHint,
  extractBusyJobId,
  overCapEntries,
  totalRegisteredCount,
  unregisteredEntries,
  applyBlockedDayMutation,
} from './logic'

const { ym } = useYearMonth()
const router = useRouter()
const { confirm } = useConfirm()

const year = computed(() => Number(ym.value.slice(0, 4)))
const calendar = useCalendar(year)
const days = computed(() => calendar.daysOf(ym.value))
const dateList = computed(() => days.value.map((d) => d.date))

const registrationKey = computed(() => `blocked-days/${ym.value}`)
const {
  data: registration,
  loading: registrationLoading,
  error: registrationError,
} = useResource(registrationKey, () => getBlockedDays(ym.value))

const feasibilityKey = computed(() => `blocked-days/${ym.value}/feasibility`)
const { data: feasibility } = useResource(feasibilityKey, () => getFeasibility(ym.value))

const staffKey = ref('staff')
const { data: staffResponse } = useResource(staffKey, () => listStaff())
const staff = computed(() => staffResponse.value?.items ?? [])
const activeStaffCount = computed(() => staff.value.filter((s) => s.status === 'active').length)

const ranksKey = ref('settings/ranks')
const { data: rankSettings } = useResource(ranksKey, () => getRankSettings())

const areasKey = ref('settings/areas')
const { data: areaSettings } = useResource(areasKey, () => getAreaSettings())

const eligibilityKey = ref('settings/eligibility-matrix')
const { data: eligibilityMatrix } = useResource(eligibilityKey, () => getEligibilityMatrix())

const scheduleKey = computed(() => `schedules/${ym.value}`)
const { data: schedule, error: scheduleError } = useResource(scheduleKey, () => getSchedule(ym.value))

const areaTypeNameByCode = computed<Record<string, string>>(() => {
  const map: Record<string, string> = {}
  for (const type of areaSettings.value?.areaTypes ?? []) map[type.code] = type.name
  return map
})

const chiefCode = computed(() =>
  chiefAreaTypeCode({
    bySupply: feasibility.value?.bySupply ?? [],
    matrix: eligibilityMatrix.value?.matrix ?? {},
    areaTypes: areaSettings.value?.areaTypes ?? [],
  }),
)
const monthlyCap = computed(() => registration.value?.monthlyCap ?? 16)
const staffNameById = computed(() => new Map(staff.value.map((s) => [s.id, s.name])))

const groups = computed(() => {
  const reg = registration.value
  if (!reg) return []
  return buildGroupViews({
    staff: staff.value,
    ranks: rankSettings.value?.ranks ?? [],
    groups: rankSettings.value?.groups ?? [],
    byStaff: reg.byStaff,
    entries: reg.entries,
    days: days.value,
    monthlyCap: monthlyCap.value,
    eligibilityMatrix: eligibilityMatrix.value?.matrix ?? {},
    areaTypeNameByCode: areaTypeNameByCode.value,
  })
})

const footerCounts = computed(() => (registration.value ? dateCountMap(registration.value.byDate, dateList.value) : new Map()))

const chiefAvailability = computed(() =>
  registration.value
    ? chiefAvailabilityByDate({
        staff: staff.value,
        matrix: eligibilityMatrix.value?.matrix ?? {},
        chiefCode: chiefCode.value,
        entries: registration.value.entries,
        days: dateList.value,
      })
    : new Map(),
)

const overList = computed(() =>
  registration.value ? overCapEntries(registration.value.byStaff, staffNameById.value) : [],
)
const noneList = computed(() =>
  registration.value ? unregisteredEntries(registration.value.byStaff, staffNameById.value) : [],
)
const totalCount = computed(() => (registration.value ? totalRegisteredCount(registration.value.byStaff) : 0))

const shortageDays = computed(() =>
  feasibility.value ? buildShortageDays(feasibility.value.byDate, chiefCode.value, areaTypeNameByCode.value) : [],
)
const wardSqueezeHint = computed(() => (feasibility.value ? evaluateWardSqueezeHint(feasibility.value.bySupply) : null))

const scheduleBadge = computed(() => {
  if (schedule.value) return `${schedule.value.status === 'published' ? '已發布' : '草稿'} v${schedule.value.revision}`
  if (scheduleError.value instanceof ApiError && scheduleError.value.status === 404) return '值班表尚未產生'
  return undefined
})

// ---------------------------------------------------------------------------
// 筆刷寫入
// ---------------------------------------------------------------------------

const brush = ref<'set' | 'clear'>('set')

const toast = ref<string | null>(null)
let toastTimer: ReturnType<typeof setTimeout> | undefined
function showToast(message: string): void {
  toast.value = message
  if (toastTimer) clearTimeout(toastTimer)
  toastTimer = setTimeout(() => {
    toast.value = null
  }, 5000)
}
onBeforeUnmount(() => {
  if (toastTimer) clearTimeout(toastTimer)
})

function isBlockedNow(staffId: string, date: string): boolean {
  return registration.value?.entries.some((e) => e.staffId === staffId && e.date === date) ?? false
}

/**
 * 拖曳中每一格的寫入都各自送出、各自 patch，彼此不等待；但一次拖曳結束時
 * （`onStrokeEnd`）要在 invalidate 前等這批還沒回來的請求全部落地，不然晚到的
 * PUT／DELETE 回應會在 invalidate 換掉 `registration` 之後才 resolve，把資料
 * patch 進一個已經被取代的舊物件、畫面再也追不上（見 `paintCell` 內的重讀防呆）。
 */
const pendingWrites = new Set<Promise<unknown>>()

async function paintCell(staffId: string, date: string): Promise<void> {
  const targetYm = ym.value
  if (!registration.value) return
  const wantBlocked = brush.value === 'set'
  if (isBlockedNow(staffId, date) === wantBlocked) return
  const writePromise = wantBlocked ? setBlockedDay(targetYm, staffId, date) : clearBlockedDay(targetYm, staffId, date)
  pendingWrites.add(writePromise)
  try {
    const result = await writePromise
    // 讀取 `.value` 要在 await 之後——這段等待期間可能有人切了月份，或
    // `onStrokeEnd` 的 invalidate() 已經把整份物件換掉；重讀一次，物件已經不是
    // 這次操作的目標月份就直接放棄，交給重抓後的真相，不要把資料寫進錯的月份。
    const reg = registration.value
    if (!reg || reg.yearMonth !== targetYm) return
    applyBlockedDayMutation(reg, staffId, date, wantBlocked, result)
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      const name = staffNameById.value.get(staffId) ?? staffId
      showToast(`${name} 已達本月不可排班日上限（${monthlyCap.value} 天），請先清除其他天再登記。`)
    } else {
      showToast(describeError(err))
    }
  } finally {
    pendingWrites.delete(writePromise)
  }
}

async function onStrokeEnd(): Promise<void> {
  // 用等待開始當下的月份，不是 await 之後的 ym.value——拖曳結束到寫入全部落地這段期間
  // 使用者可能已經切了月份，invalidate 要對準這次拖曳真正操作的那個月，不是畫面現在停在哪個月。
  const targetYm = ym.value
  await Promise.allSettled([...pendingWrites])
  void invalidate(`blocked-days/${targetYm}`)
}

// ---------------------------------------------------------------------------
// 帶入求解
// ---------------------------------------------------------------------------

async function onSolve(): Promise<void> {
  const ok = await confirm({
    title: '帶入求解',
    message: `以 ${ym.value} 目前的不可排班日登記建立求解工作，依序求解 3 份候選值班表，需要一些時間。確定要開始嗎？`,
    confirmText: '開始求解',
  })
  if (!ok) return

  try {
    // openapi-typescript 把有 `default` 的欄位視為必填（見 schema.d.ts），
    // 三個欄位都逐字照 api-contract.yaml 的預設值（variantCount 3、timeLimitSecPerVariant 15）明寫出來。
    const job = await createSolverJob({ yearMonth: ym.value, variantCount: 3, timeLimitSecPerVariant: 15 })
    void router.push({ name: 'variants', params: { ym: ym.value }, query: { job: job.jobId } })
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      showToast('已有求解工作在執行中，帶你去看那一個。')
      const busyJobId = extractBusyJobId(err.body)
      void router.push({
        name: 'variants',
        params: { ym: ym.value },
        query: busyJobId ? { job: busyJobId } : {},
      })
      return
    }
    showToast(describeError(err))
  }
}
</script>

<template>
  <PageLayout :title="`${ym} 不可排班日登記`" :subtitle="scheduleBadge">
    <template #actions>
      <div class="brush-toggle">
        <button
          type="button"
          class="brush-toggle__btn"
          :class="{ 'brush-toggle__btn--active': brush === 'set' }"
          @click="brush = 'set'"
        >
          筆刷：登記
        </button>
        <button
          type="button"
          class="brush-toggle__btn"
          :class="{ 'brush-toggle__btn--active': brush === 'clear' }"
          @click="brush = 'clear'"
        >
          清除
        </button>
      </div>
      <button type="button" class="btn btn-primary" @click="onSolve">帶入求解</button>
    </template>

    <div v-if="toast" class="toast" role="alert">
      <span>{{ toast }}</span>
      <button type="button" class="toast__close" @click="toast = null">關閉</button>
    </div>

    <p v-if="registrationLoading && !registration">載入不可排班日登記中…</p>
    <p v-else-if="registrationError">{{ describeError(registrationError) }}</p>
    <div v-else class="blocked-days">
      <BlockedDayMatrix
        :groups="groups"
        :days="days"
        :footer-counts="footerCounts"
        :chief-availability="chiefAvailability"
        :monthly-cap="monthlyCap"
        @paint="paintCell"
        @stroke-end="onStrokeEnd"
      />
      <BlockedDaySidebar
        :total-count="totalCount"
        :active-staff-count="activeStaffCount"
        :monthly-cap="monthlyCap"
        :over-list="overList"
        :none-list="noneList"
        :shortage-days="shortageDays"
        :by-supply="feasibility?.bySupply ?? []"
        :area-type-name-by-code="areaTypeNameByCode"
        :ward-squeeze-hint="wardSqueezeHint"
      />
    </div>
  </PageLayout>
</template>

<style scoped>
.brush-toggle {
  display: flex;
}

.brush-toggle__btn {
  flex: none;
  padding: 6px 12px;
  font-family: var(--font-heading);
  font-weight: 600;
  font-size: 12px;
  cursor: pointer;
  background: transparent;
  border: 1px solid color-mix(in srgb, var(--color-text) 16%, transparent);
  color: var(--color-text);
}

.brush-toggle__btn:first-child {
  border-right-width: 0;
}

.brush-toggle__btn--active {
  background: var(--color-accent);
  border-color: var(--color-accent);
  color: var(--color-bg);
}

.toast {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  margin-bottom: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--color-accent);
  background: color-mix(in srgb, var(--color-accent) 10%, transparent);
  color: var(--color-accent-900);
  font-size: 12px;
}

.toast__close {
  margin-left: auto;
  flex: none;
  background: none;
  border: none;
  cursor: pointer;
  font: inherit;
  color: inherit;
  text-decoration: underline;
}

.blocked-days {
  display: flex;
  gap: var(--space-4);
  align-items: flex-start;
  height: 100%;
}
</style>
