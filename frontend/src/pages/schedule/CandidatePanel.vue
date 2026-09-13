<script setup lang="ts">
/**
 * 點格開的候選人面板。`blockingReasons` 標紅但仍可選（寫入不會被拒絕，由排班者決定，
 * api-contract.yaml `Candidate` 的說明）。選定後 `PATCH duties`；也能「清空此格」。
 *
 * 元件自己打 `listCandidates`／`setDuty`，成功後 emit `assigned` 讓外層 `invalidate`
 * 並關閉面板——這裡不直接 import `invalidate`，避免這顆元件對快取 key 慣例做假設。
 */
import { computed, ref } from 'vue'
import { listCandidates } from '@/api/views'
import { setDuty } from '@/api/schedules'
import { ApiError } from '@/api/client'
import { describeError } from '@/api/errors'
import { useResource } from '@/composables/useResource'
import { abbreviate } from './lib/scheduleGrid'

const props = defineProps<{
  ym: string
  areaId: string
  areaLabel: string
  date: string
  /** 目前這一格已指派的人（若有），用於「清空此格」判斷是否要顯示。 */
  currentStaffId: string | null
}>()

const emit = defineEmits<{ close: []; assigned: [] }>()

const key = computed(() => `schedules/${props.ym}/candidates/${props.areaId}/${props.date}`)
const { data, loading, error } = useResource(key, () => listCandidates(props.ym, props.areaId, props.date))

const submitting = ref(false)
const submitError = ref<string | null>(null)

async function pick(staffId: string | null): Promise<void> {
  if (submitting.value) return
  submitting.value = true
  submitError.value = null
  try {
    await setDuty(props.ym, { areaId: props.areaId, date: props.date, staffId })
    emit('assigned')
  } catch (err) {
    submitError.value = describeError(err)
  } finally {
    submitting.value = false
  }
}

const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404)
</script>

<template>
  <div class="candidate-panel__backdrop" @click.self="emit('close')">
    <div class="candidate-panel" role="dialog" aria-modal="true">
      <header class="candidate-panel__header">
        <div>
          <div class="candidate-panel__title">{{ areaLabel }} · {{ date }}</div>
          <div class="candidate-panel__subtitle">剩餘額度 · 同區延續比例 · 阻擋理由（標紅仍可選）</div>
        </div>
        <button type="button" class="candidate-panel__close" aria-label="關閉" @click="emit('close')">×</button>
      </header>

      <p v-if="submitError" class="candidate-panel__error">{{ submitError }}</p>

      <div v-if="loading" class="candidate-panel__state">載入候選人中…</div>
      <div v-else-if="notFound" class="candidate-panel__state">該月尚無值班表，無法列出候選人。</div>
      <div v-else-if="error" class="candidate-panel__state">{{ describeError(error) }}</div>
      <ul v-else class="candidate-panel__list">
        <li
          v-for="candidate in data?.candidates ?? []"
          :key="candidate.staffId"
          class="candidate-panel__row"
          :class="{ 'candidate-panel__row--blocked': candidate.blockingReasons.length > 0 }"
        >
          <button type="button" class="candidate-panel__pick" :disabled="submitting" @click="pick(candidate.staffId)">
            <span class="candidate-panel__ab">{{ abbreviate(candidate.name) }}</span>
            <span class="candidate-panel__name">{{ candidate.name }}</span>
            <span class="candidate-panel__rank">{{ candidate.rankCode }}</span>
            <span class="candidate-panel__remaining">
              {{ candidate.quotaRemaining === null ? '不計' : `餘 ${candidate.quotaRemaining}` }}
            </span>
            <span class="candidate-panel__consistency">
              同區 {{ Math.round((candidate.areaConsistency ?? 0) * 100) }}%
            </span>
          </button>
          <ul v-if="candidate.blockingReasons.length > 0" class="candidate-panel__reasons candidate-panel__reasons--blocking">
            <li v-for="reason in candidate.blockingReasons" :key="reason">{{ reason }}</li>
          </ul>
          <ul v-if="candidate.warnings?.length" class="candidate-panel__reasons">
            <li v-for="warning in candidate.warnings" :key="warning">{{ warning }}</li>
          </ul>
        </li>
        <li v-if="(data?.candidates.length ?? 0) === 0" class="candidate-panel__state">沒有符合資格的候選人。</li>
      </ul>

      <footer class="candidate-panel__footer">
        <button
          v-if="currentStaffId"
          type="button"
          class="btn btn-secondary"
          :disabled="submitting"
          @click="pick(null)"
        >
          清空此格
        </button>
        <button type="button" class="btn btn-secondary" @click="emit('close')">取消</button>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.candidate-panel__backdrop {
  position: fixed;
  inset: 0;
  background: color-mix(in srgb, var(--color-text) 35%, transparent);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 40;
}

.candidate-panel {
  width: 420px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  background: var(--color-bg);
  border: 1px solid var(--color-divider);
  box-shadow: var(--shadow-lg);
}

.candidate-panel__header {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-4);
  border-bottom: 1px solid var(--color-divider);
}

.candidate-panel__title {
  font-family: var(--font-heading);
  font-weight: 600;
  font-size: 15px;
}

.candidate-panel__subtitle {
  margin-top: 2px;
  font-size: 11px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.candidate-panel__close {
  margin-left: auto;
  width: 24px;
  height: 24px;
  flex: none;
  border: none;
  background: transparent;
  font-size: 16px;
  line-height: 1;
  cursor: pointer;
  color: var(--color-text);
}

.candidate-panel__error {
  margin: var(--space-2) var(--space-4) 0;
  padding: var(--space-2);
  font-size: 12px;
  background: var(--color-accent-100);
  color: var(--color-accent-900);
  border: 1px solid var(--color-accent);
}

.candidate-panel__state {
  padding: var(--space-4);
  font-size: 12px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.candidate-panel__list {
  list-style: none;
  margin: 0;
  padding: var(--space-2) var(--space-2);
  overflow-y: auto;
  flex: 1;
  min-height: 0;
}

.candidate-panel__row {
  padding: var(--space-1) 0;
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
}

.candidate-panel__pick {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  padding: var(--space-2);
  background: transparent;
  border: none;
  cursor: pointer;
  text-align: left;
  font-size: 12.5px;
  color: var(--color-text);
}

.candidate-panel__pick:hover {
  background: color-mix(in srgb, var(--color-text) 6%, transparent);
}

.candidate-panel__ab {
  flex: none;
  width: 24px;
  height: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  font: 600 11px var(--font-heading);
  background: var(--color-accent-100);
  color: var(--color-accent-800);
}

.candidate-panel__name {
  flex: none;
  min-width: 52px;
}

.candidate-panel__rank {
  flex: none;
  font: 600 10px var(--font-heading);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.candidate-panel__remaining {
  margin-left: auto;
  flex: none;
  font: 600 11px var(--font-heading);
}

.candidate-panel__consistency {
  flex: none;
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.candidate-panel__row--blocked .candidate-panel__pick {
  background: color-mix(in srgb, var(--color-accent) 8%, transparent);
}

.candidate-panel__reasons {
  margin: 0 0 var(--space-1) 56px;
  padding: 0;
  list-style: none;
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.candidate-panel__reasons--blocking {
  color: var(--color-accent-800);
}

.candidate-panel__footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-4);
  border-top: 1px solid var(--color-divider);
}
</style>
