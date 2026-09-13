<script setup lang="ts">
/**
 * SCREEN 04 變體比較 ＋ 04b 求解進度覆蓋層（issue #30）。
 *
 * `GET /solver-jobs/{jobId}` 是真相來源：先 GET 一次（工作可能在訂閱前就結束），
 * 已經是終態就不訂閱（正式版的 WebView2 host message 沒有補送，訂閱了也等不到事件），
 * 還在跑的才 `subscribe`。收到終態事件後再 GET 一次拿最終數字，才去抓 `/variants`
 * （frontend-plan.md §3.5、ARCHITECTURE §4.7／§4.8）。
 */
import { computed, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import PageLayout from '@/components/PageLayout.vue'
import { useYearMonth } from '@/composables/useYearMonth'
import { useConfirm } from '@/composables/useConfirm'
import { invalidate, useResource } from '@/composables/useResource'
import { describeError } from '@/api/errors'
import { ApiError } from '@/api/client'
import { getAreaSettings, getConstraints, getRankSettings } from '@/api/settings'
import { listStaff } from '@/api/staff'
import { applyVariant, getSchedule } from '@/api/schedules'
import { cancelSolverJob, createSolverJob, getSolverJob, listVariants } from '@/api/solver'
import { subscribe, type Unsubscribe } from '@/realtime'
import type { SolverJob, SolverProgress, Variant } from '@/api/types'
import { daysInMonth } from './dates'
import { diffVariants } from './diff'
import { staffGroupIndex } from './heatmap'
import { readLastJobId, rememberJobId, resolveJobId } from './jobStorage'
import ProgressOverlay from './ProgressOverlay.vue'
import VariantCard from './VariantCard.vue'
import {
  acceptPolledSnapshot,
  buildMetricRows,
  describeFailure,
  formatSeconds,
  isFairnessPointEnabled,
  isTerminalStatus,
  shortJobId,
} from './variantView'

const route = useRoute()
const router = useRouter()
const { ym } = useYearMonth()
const { confirm } = useConfirm()

// ---------------------------------------------------------------------------
// 設定與名冊：跟其他畫面共用同一份 useResource 快取 key，不各自重抓。
// ---------------------------------------------------------------------------
const areaSettings = useResource(
  computed(() => 'settings/areas'),
  () => getAreaSettings(),
)
const rankSettings = useResource(
  computed(() => 'settings/ranks'),
  () => getRankSettings(),
)
const constraintSettings = useResource(
  computed(() => 'settings/constraints'),
  () => getConstraints(),
)
const staffResource = useResource(
  computed(() => 'staff'),
  () => listStaff(),
)
const scheduleResource = useResource(
  computed(() => `schedules/${ym.value}`),
  () => getSchedule(ym.value),
)

const areas = computed(() => areaSettings.data.value?.areas ?? [])
const areaIds = computed(() => areas.value.map((a) => a.id))
const areaLabelById = computed(() => new Map(areas.value.map((a) => [a.id, a.name])))
const days = computed(() => daysInMonth(ym.value))
const groupIndexByStaff = computed(() =>
  staffGroupIndex(
    staffResource.data.value?.items ?? [],
    rankSettings.data.value?.ranks ?? [],
    rankSettings.data.value?.groups ?? [],
  ),
)
const staffNameById = computed(() => new Map((staffResource.data.value?.items ?? []).map((s) => [s.id, s.name])))
const showFairnessPoint = computed(() => isFairnessPointEnabled(constraintSettings.data.value?.soft))
// 該月已發布時套用鈕停用，不讓使用者撞 409 SCHEDULE_ALREADY_PUBLISHED。
const schedulePublished = computed(() => scheduleResource.data.value?.status === 'published')

// ---------------------------------------------------------------------------
// 求解工作狀態
// ---------------------------------------------------------------------------
const jobId = ref<string | null>(null)
const job = ref<SolverJob | null>(null)
const jobLoading = ref(false)
const jobNotice = ref<string | null>(null)
const variants = ref<Variant[]>([])
const variantsError = ref<unknown>(null)
// 提前宣告：下面的 `loadForYearMonth` 在 immediate watcher 觸發時是同步呼叫到這兩個 ref，
// 若宣告留在檔案後段（套用／逐格差異區塊）會撞 TDZ（ReferenceError）。
const applyError = ref<string | null>(null)
const diffSelection = ref<string[]>([])

let unsubscribe: Unsubscribe | null = null
let watchdogTimer: ReturnType<typeof setInterval> | null = null

function teardownSubscription(): void {
  unsubscribe?.()
  unsubscribe = null
  stopWatchdog()
}

function stopWatchdog(): void {
  if (watchdogTimer === null) return
  clearInterval(watchdogTimer)
  watchdogTimer = null
}

/**
 * 覆蓋層可見時的補班：正式版 WebView2 host message 沒有「補送」機制，
 * `GET /solver-jobs/{id}` 回 running 與 `subscribe` 之間工作剛好結束的話，
 * 覆蓋層會停在最後一筆進度，靠 5 秒一次的輪詢把它撈回終態（issue #46）。
 */
async function pollJobOnce(id: string, targetYm: string): Promise<void> {
  try {
    const result = await getSolverJob(id)
    if (ym.value !== targetYm || jobId.value !== id || !job.value) return
    // SSE 的終態事件可能搶在這支較舊的輪詢回應前面落地（cancelJob 等最多 10 秒才回、
    // refreshAfterTerminal 也會再 GET 一次）：終態是單向門，已經到終態就不接受回退。
    if (!acceptPolledSnapshot(job.value.status)) return
    job.value = result
    if (isTerminalStatus(result.status)) {
      teardownSubscription()
      await loadVariants(id, targetYm)
    }
  } catch {
    // 看門狗容忍暫時性錯誤，等下一次 tick 再試，不覆蓋目前顯示的畫面。
  }
}

function startWatchdog(id: string, targetYm: string): void {
  stopWatchdog()
  watchdogTimer = setInterval(() => void pollJobOnce(id, targetYm), 5000)
}

function clearQueryJob(): void {
  const query = { ...route.query }
  delete query.job
  void router.replace({ query })
}

/**
 * 換一份工作（換月份或重新求解）時要清掉的畫面狀態。變體 id 固定是
 * v-a/v-b/v-c（ADR-0003），不清的話舊工作選的逐格差異／套用錯誤會誤套到新工作同名的變體上。
 */
function resetJobDisplayState(): void {
  variants.value = []
  variantsError.value = null
  diffSelection.value = []
  applyError.value = null
}

async function loadVariants(id: string, targetYm: string): Promise<void> {
  variantsError.value = null
  try {
    const res = await listVariants(id)
    if (ym.value !== targetYm) return
    variants.value = res.variants
  } catch (err) {
    if (ym.value !== targetYm) return
    variantsError.value = err
  }
}

async function refreshAfterTerminal(id: string, targetYm: string): Promise<void> {
  try {
    const result = await getSolverJob(id)
    if (ym.value === targetYm && jobId.value === id) job.value = result
  } catch {
    // 結束時再 GET 失敗——保留目前顯示的最後一份快照，不讓畫面整個消失。
  }
  await loadVariants(id, targetYm)
}

function handleProgressEvent(id: string, targetYm: string, event: SolverProgress): void {
  if (ym.value !== targetYm || jobId.value !== id || !job.value) return
  if (isTerminalStatus(job.value.status)) return // 已經在終態，訂閱理應已拆——多一層保險，不讓事件把數字退回去
  job.value = { ...job.value, status: event.status, progress: event }
  if (isTerminalStatus(event.status)) {
    teardownSubscription()
    void refreshAfterTerminal(id, targetYm)
  }
}

type AttachOutcome = 'attached' | 'mismatch' | 'not-found' | 'error'

/**
 * GET 一次決定要不要訂閱：已經是終態的工作不 `subscribe`——正式版的
 * WebView2 host message 沒有「補送」，訂閱一個已經結束的工作只會空等。
 *
 * 只有真的接上（`attached`）才會 `rememberJobId`／清畫面狀態——呼叫端不該在
 * 呼叫前先寫 localStorage 或改 URL query，否則 attach 失敗時（例如 409 給的
 * busy jobId 屬於別的月份）localStorage 與網址會被寫成一個畫面上根本沒有顯示的
 * jobId。
 */
async function attachJob(id: string, targetYm: string): Promise<AttachOutcome> {
  jobLoading.value = true
  try {
    const result = await getSolverJob(id)
    if (ym.value !== targetYm) return 'mismatch'
    if (result.yearMonth !== targetYm) return 'mismatch'

    // 換一份工作：變體 id 固定是 v-a/v-b/v-c（ADR-0003），舊工作選的逐格差異／
    // 套用錯誤／變體列表不清掉的話，會誤套到新工作同名的變體上。
    if (jobId.value !== id) resetJobDisplayState()
    jobId.value = id
    job.value = result
    rememberJobId(targetYm, id)

    if (isTerminalStatus(result.status)) {
      await loadVariants(id, targetYm)
    } else {
      unsubscribe = subscribe(id, (event) => handleProgressEvent(id, targetYm, event))
      startWatchdog(id, targetYm)
    }
    return 'attached'
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return 'not-found'
    // 網路／伺服器錯誤不是「這個 jobId 不存在」，不該清掉 `?job=` 或改當作 not-found
    // 去試 localStorage 的備援——只是把訊息顯示出來，讓使用者自己重新整理或重試。
    jobNotice.value = describeError(err)
    return 'error'
  } finally {
    if (ym.value === targetYm) jobLoading.value = false
  }
}

async function loadForYearMonth(targetYm: string): Promise<void> {
  teardownSubscription()
  jobId.value = null
  job.value = null
  jobLoading.value = false // 上一次呼叫若中途被切月份中斷，finally 的守衛會讓它卡在 true
  jobNotice.value = null
  resetJobDisplayState()

  const candidate = resolveJobId(route.query.job, targetYm)
  if (!candidate) return

  let outcome = await attachJob(candidate, targetYm)
  if (ym.value !== targetYm) return

  if ((outcome === 'not-found' || outcome === 'mismatch') && route.query.job) {
    // query 帶的 jobId 查無此工作、或屬於別的月份（例如重整前切過月份殘留的舊 query）——
    // 清掉 query，退回 localStorage 記的那一個。`error`（網路／伺服器錯誤）不屬於這裡，
    // attachJob 已經把訊息寫進 jobNotice，不清 query、也不猜備援。
    clearQueryJob()
    const fallback = readLastJobId(targetYm)
    if (fallback && fallback !== candidate) {
      outcome = await attachJob(fallback, targetYm)
      if (ym.value !== targetYm) return
    }
  }

  if (outcome !== 'attached' && !jobNotice.value) {
    jobNotice.value = '找不到可顯示的求解工作，登記完成後可以直接求解。'
  }
}

watch(
  ym,
  (currentYm, _previous, onCleanup) => {
    void loadForYearMonth(currentYm)
    onCleanup(() => teardownSubscription())
  },
  { immediate: true },
)

onUnmounted(() => teardownSubscription())

// ---------------------------------------------------------------------------
// 空狀態／失敗／中止：直接求解或重新求解
// ---------------------------------------------------------------------------
const creatingJob = ref(false)
const createJobError = ref<string | null>(null)

async function startSolve(): Promise<void> {
  const ok = await confirm({
    title: '直接求解',
    message: `以目前的登記與設定為 ${ym.value} 求解三份變體，沒有登記期機制，確定要開始嗎？`,
    confirmText: '開始求解',
  })
  if (!ok) return

  creatingJob.value = true
  createJobError.value = null
  try {
    // variantCount／timeLimitSecPerVariant 照 CreateSolverJobRequest 的契約預設值
    // （openapi-typescript 把有 `default` 的欄位生成成必填，這裡明寫預設值而非改生成設定）。
    const created = await createSolverJob({ yearMonth: ym.value, variantCount: 3, timeLimitSecPerVariant: 15 })
    // 只有真的接上才改網址 query／記 localStorage（attachJob 內部處理），避免使用者在
    // 等待建立回應時已經切換月份，這裡把新工作的 jobId 誤寫進另一個月份的網址。
    const outcome = await attachJob(created.jobId, ym.value)
    if (outcome === 'attached') {
      await router.replace({ query: { ...route.query, job: created.jobId } })
    }
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      // 單一 slot：全系統同時只有一份求解工作在跑，409 附的 busy jobId 可能屬於
      // 別的月份。先 attach 再決定要不要改網址；attach 不成就用「已有求解工作
      // 正在執行中」的訊息，不要在使用者看的畫面上偷偷換成別月的工作。
      const details = (err.body as { error?: { details?: { jobId?: string } } } | null)?.error?.details
      if (details?.jobId) {
        const outcome = await attachJob(details.jobId, ym.value)
        if (outcome === 'attached') {
          await router.replace({ query: { ...route.query, job: details.jobId } })
        } else {
          createJobError.value = describeError(err)
        }
        creatingJob.value = false
        return
      }
    }
    createJobError.value = describeError(err)
  } finally {
    creatingJob.value = false
  }
}

// ---------------------------------------------------------------------------
// 04b：中止——保留已完成的變體
// ---------------------------------------------------------------------------
const cancelling = ref(false)
const cancelError = ref<string | null>(null)

/**
 * 後端 `SolverJobService.CancelAsync` 會等最多 10 秒讓求解器優雅停止才回應，
 * 所以**不要**在送出 DELETE 前就 `teardownSubscription`——訂閱還留著，這段等待
 * 期間如果有進度事件飄過來，畫面還能照樣更新，不會空白凍結。以 DELETE 的回應
 * 為準；只有回應已經是終態才拆訂閱、抓 `/variants`（理論上 DELETE 回應必是終態，
 * 這裡多一層防呆，不假設後端一定照約定回）。
 */
async function cancelJob(): Promise<void> {
  if (!jobId.value || cancelling.value) return
  const id = jobId.value
  const targetYm = ym.value
  cancelling.value = true
  cancelError.value = null
  try {
    const result = await cancelSolverJob(id)
    if (ym.value !== targetYm || jobId.value !== id) return
    job.value = result
    if (isTerminalStatus(result.status)) {
      teardownSubscription()
      await loadVariants(id, targetYm)
    }
  } catch (err) {
    if (ym.value !== targetYm || jobId.value !== id) return
    cancelError.value = describeError(err)
  } finally {
    if (ym.value === targetYm && jobId.value === id) cancelling.value = false
  }
}

// ---------------------------------------------------------------------------
// 套用變體
// ---------------------------------------------------------------------------
const applyingVariantId = ref<string | null>(null)

async function applyToSchedule(variantId: string): Promise<void> {
  if (!jobId.value || schedulePublished.value) return
  applyingVariantId.value = variantId
  applyError.value = null
  try {
    await applyVariant(ym.value, { jobId: jobId.value, variantId })
    await invalidate(`schedules/${ym.value}`)
    await router.push({ name: 'schedule', params: { ym: ym.value } })
  } catch (err) {
    applyError.value = describeError(err)
  } finally {
    applyingVariantId.value = null
  }
}

// ---------------------------------------------------------------------------
// 逐格差異：選兩份變體就地比對，不打額外端點
// ---------------------------------------------------------------------------
function toggleDiffSelection(variantId: string): void {
  if (diffSelection.value.includes(variantId)) {
    diffSelection.value = diffSelection.value.filter((id) => id !== variantId)
    return
  }
  const next = [...diffSelection.value, variantId]
  diffSelection.value = next.length > 2 ? next.slice(next.length - 2) : next
}

const diffPair = computed(() => {
  if (diffSelection.value.length !== 2) return null
  const [aId, bId] = diffSelection.value
  const a = variants.value.find((v) => v.id === aId)
  const b = variants.value.find((v) => v.id === bId)
  return a && b ? { a, b } : null
})

const diffResult = computed(() => (diffPair.value ? diffVariants(diffPair.value.a.duties, diffPair.value.b.duties) : []))

function staffLabel(staffId: string | null): string {
  if (staffId === null) return '（空缺）'
  return staffNameById.value.get(staffId) ?? staffId
}

// ---------------------------------------------------------------------------
// 衍生狀態
// ---------------------------------------------------------------------------
// 每個指標在這批變體裡的最大值，給 VariantCard 的長條當分母（跨變體同指標互比，
// 而不是同一張卡片內不同單位的指標互比）。
const metricMax = computed(() => {
  const max: Record<string, number> = {}
  for (const variant of variants.value) {
    for (const row of buildMetricRows(variant.metrics, showFairnessPoint.value)) {
      max[row.key] = Math.max(max[row.key] ?? 0, Math.abs(row.value))
    }
  }
  return max
})

const overlayVisible = computed(() => job.value?.status === 'queued' || job.value?.status === 'running')
const failureText = computed(() => describeFailure(job.value?.failureReason))
const showEmptyState = computed(() => !jobId.value && !jobLoading.value)
const variantListEmptyNote = computed(() => {
  if (job.value?.status === 'cancelled' || job.value?.status === 'failed') return '尚無已完成的變體，可以重新求解。'
  return '尚無已完成的變體。'
})
</script>

<template>
  <PageLayout title="變體比較" :subtitle="`年月：${ym}`">
    <template #actions>
      <RouterLink class="btn btn-secondary" :to="{ name: 'blockedDays', params: { ym } }">
        去登記不可排班日
      </RouterLink>
    </template>

    <div class="variants-page">
      <section v-if="showEmptyState" class="empty-state">
        <p class="empty-state__notice">{{ jobNotice ?? '尚無求解工作。' }}</p>
        <p class="empty-state__hint">
          先到「不可排班日登記」把這個月的登記帶入求解，或直接用目前的登記與設定求解。
        </p>
        <div class="empty-state__actions">
          <RouterLink class="btn btn-secondary" :to="{ name: 'blockedDays', params: { ym } }">
            去登記不可排班日
          </RouterLink>
          <button type="button" class="btn btn-primary" :disabled="creatingJob" @click="startSolve">
            {{ creatingJob ? '求解中…' : '直接求解' }}
          </button>
        </div>
        <p v-if="createJobError" class="notice notice--error">{{ createJobError }}</p>
      </section>

      <template v-else-if="job">
        <header class="job-header">
          <div class="job-header__title">SOLVER · 變體比較</div>
          <div class="job-header__meta">
            {{ shortJobId(job.jobId) }} · 耗時 {{ formatSeconds(job.elapsedSec) }} ·
            {{ job.scale?.areas ?? '—' }} 區 × {{ job.scale?.days ?? '—' }} 日 = {{ job.scale?.variables ?? '—' }}
            個指派
          </div>
          <div class="job-header__constraints">
            硬約束 {{ job.constraintCount?.hard ?? '—' }} · 軟約束 {{ job.constraintCount?.soft ?? '—' }}
          </div>
        </header>

        <ul v-if="job.warnings?.length" class="job-warnings">
          <li v-for="(warning, i) in job.warnings" :key="i" class="job-warnings__item">
            <span class="tag tag-accent">非阻斷</span>
            <span>{{ warning }}</span>
          </li>
        </ul>

        <p v-if="job.status === 'failed'" class="notice notice--error">求解失敗：{{ failureText }}</p>
        <p v-if="job.status === 'cancelled'" class="notice">已中止，以下是已完成的變體，仍可套用。</p>
        <p v-if="variantsError" class="notice notice--error">{{ describeError(variantsError) }}</p>
        <p v-if="applyError" class="notice notice--error">{{ applyError }}</p>

        <div v-if="job.status === 'failed' || job.status === 'cancelled'" class="job-header__actions">
          <button type="button" class="btn btn-secondary" :disabled="creatingJob" @click="startSolve">
            {{ creatingJob ? '求解中…' : '重新求解' }}
          </button>
          <span v-if="createJobError" class="notice notice--error">{{ createJobError }}</span>
        </div>

        <div v-if="variants.length" class="variant-list">
          <VariantCard
            v-for="variant in variants"
            :key="variant.id"
            :variant="variant"
            :area-ids="areaIds"
            :area-label-by-id="areaLabelById"
            :days="days"
            :group-index-by-staff="groupIndexByStaff"
            :show-fairness-point="showFairnessPoint"
            :metric-max="metricMax"
            :diff-selected="diffSelection.includes(variant.id)"
            :applying="applyingVariantId === variant.id"
            :apply-disabled="schedulePublished"
            :apply-disabled-reason="schedulePublished ? '已發布的值班表只能逐格改，不能整份套用變體' : null"
            @apply="applyToSchedule(variant.id)"
            @toggle-diff="toggleDiffSelection(variant.id)"
          />
        </div>
        <p v-else-if="!overlayVisible" class="variant-list__empty">{{ variantListEmptyNote }}</p>

        <section v-if="diffPair" class="diff-panel">
          <h4 class="diff-panel__title">
            逐格差異：{{ diffPair.a.label }}（{{ diffPair.a.id }}）vs {{ diffPair.b.label }}（{{ diffPair.b.id }}）
          </h4>
          <p class="diff-panel__count">共 {{ diffResult.length }} 格不同</p>
          <ul v-if="diffResult.length" class="diff-panel__list">
            <li v-for="d in diffResult" :key="d.cellKey">
              {{ d.date }} · {{ areaLabelById.get(d.areaId) ?? d.areaId }}：{{ staffLabel(d.from) }} →
              {{ staffLabel(d.to) }}
            </li>
          </ul>
        </section>
      </template>

      <p v-else class="notice">載入求解工作中…</p>
    </div>

    <ProgressOverlay
      v-if="overlayVisible && job"
      :job="job"
      :cancelling="cancelling"
      :cancel-error="cancelError"
      @cancel="cancelJob"
    />
  </PageLayout>
</template>

<style scoped>
.variants-page {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.empty-state {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  align-items: flex-start;
  max-width: 560px;
}

.empty-state__notice {
  margin: 0;
  font-size: 16px;
  font-family: var(--font-heading);
}

.empty-state__hint {
  margin: 0;
  font-size: 13px;
  color: color-mix(in srgb, var(--color-text) 60%, transparent);
}

.empty-state__actions {
  display: flex;
  gap: var(--space-2);
  margin-top: var(--space-2);
}

.job-header {
  padding-bottom: var(--space-2);
  border-bottom: 1px solid var(--color-divider);
}

.job-header__title {
  font-family: var(--font-heading);
  font-size: 15px;
  letter-spacing: 0.08em;
}

.job-header__meta,
.job-header__constraints {
  font-size: 11.5px;
  color: color-mix(in srgb, var(--color-text) 60%, transparent);
  margin-top: 2px;
}

.job-header__actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.job-warnings {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.job-warnings__item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11.5px;
}

.notice {
  margin: 0;
  font-size: 12.5px;
  padding: 6px 8px;
  border: 1px solid var(--color-divider);
  background: var(--color-surface);
}

.notice--error {
  border-color: var(--color-accent-900);
  color: var(--color-accent-900);
  background: color-mix(in srgb, var(--color-accent-900) 8%, transparent);
}

.variant-list {
  display: flex;
  gap: var(--space-3);
  flex-wrap: wrap;
}

.variant-list__empty {
  font-size: 13px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.diff-panel {
  border: 1px solid var(--color-divider);
  padding: var(--space-3);
}

.diff-panel__title {
  margin: 0 0 4px;
  font-size: 13px;
}

.diff-panel__count {
  margin: 0 0 var(--space-2);
  font-size: 12px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.diff-panel__list {
  margin: 0;
  padding-left: 18px;
  font-size: 12px;
  display: flex;
  flex-direction: column;
  gap: 3px;
  max-height: 260px;
  overflow: auto;
}
</style>
