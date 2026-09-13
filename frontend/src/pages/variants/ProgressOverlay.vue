<script setup lang="ts">
/**
 * SCREEN 04b：求解進度覆蓋層。只在 job.status 為 queued／running 時掛載
 * （由父層 v-if 控制，這裡不重複判斷）。不提供完成百分比（ARCHITECTURE §4.7）。
 */
import { computed } from 'vue'
import type { SolverJob } from '@/api/types'
import { VARIANT_SLOT_IDS, formatGap, formatSeconds, progressHeadline, shortJobId, variantSlotStatuses } from './variantView'

const props = defineProps<{ job: SolverJob }>()
const emit = defineEmits<{ cancel: [] }>()

const progress = computed(() => props.job.progress)
const variantIndex = computed(() => progress.value?.variantIndex ?? 0)
const variantCount = computed(() => props.job.variantCount)

const headline = computed(() => progressHeadline(props.job.status, variantIndex.value, variantCount.value))
const slots = computed(() =>
  variantSlotStatuses(variantCount.value, variantIndex.value).map((status, i) => ({
    id: VARIANT_SLOT_IDS[i] ?? `v-${i + 1}`,
    status,
  })),
)

const slotLabel: Record<string, string> = { done: '完成', running: '求解中', waiting: '等待' }
</script>

<template>
  <div class="progress-overlay">
    <div class="progress-overlay__panel">
      <div class="progress-overlay__kicker">求解中 · {{ shortJobId(job.jobId) }}</div>
      <div class="progress-overlay__headline">{{ headline }}</div>

      <div class="progress-overlay__stats">
        <div class="progress-overlay__stat">
          <div class="progress-overlay__stat-label">已耗時 / 上限</div>
          <div class="progress-overlay__stat-value">
            {{ formatSeconds(progress?.elapsedSec) }}
            <span class="progress-overlay__stat-suffix">/ {{ progress?.timeLimitSec ?? '—' }}s</span>
          </div>
        </div>
        <div class="progress-overlay__stat">
          <div class="progress-overlay__stat-label">本份候選解</div>
          <div class="progress-overlay__stat-value">
            {{ progress?.solutionCount ?? 0 }} <span class="progress-overlay__stat-suffix">個</span>
          </div>
        </div>
        <div class="progress-overlay__stat">
          <div class="progress-overlay__stat-label">收斂間隙 gap</div>
          <div class="progress-overlay__stat-value">{{ formatGap(progress?.gap) }}</div>
        </div>
      </div>

      <p class="progress-overlay__note">
        gap 會上下跳動、不單調，0 代表已證明最佳。CP-SAT 給不出可信的完成度，所以這裡沒有百分比進度條。
      </p>

      <div class="progress-overlay__footer">
        <div class="progress-overlay__slots">
          <span
            v-for="slot in slots"
            :key="slot.id"
            class="progress-overlay__slot"
            :class="`progress-overlay__slot--${slot.status}`"
          >
            {{ slot.id }} {{ slotLabel[slot.status] }}
          </span>
        </div>
        <button type="button" class="btn btn-secondary" @click="emit('cancel')">中止</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.progress-overlay {
  /* 固定在整個視窗，不是頁面本體——PageLayout 的內容區有自己的捲動，
     覆蓋層不該跟著它捲走。 */
  position: fixed;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, var(--color-neutral-900) 45%, transparent);
  z-index: 20;
}

.progress-overlay__panel {
  width: min(420px, calc(100vw - var(--space-8)));
  padding: var(--space-4) var(--space-6);
  background: var(--color-bg);
  border: 1px solid var(--color-accent);
  box-shadow: var(--shadow-lg);
}

.progress-overlay__kicker {
  font-family: var(--font-heading);
  font-size: 10px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  margin-bottom: var(--space-2);
}

.progress-overlay__headline {
  font-family: var(--font-heading);
  font-size: 26px;
  letter-spacing: 0.02em;
}

.progress-overlay__stats {
  display: flex;
  gap: var(--space-2);
  margin-top: var(--space-4);
}

.progress-overlay__stat {
  flex: 1;
  border: 1px solid var(--color-divider);
  padding: 8px 9px;
}

.progress-overlay__stat-label {
  font-family: var(--font-heading);
  font-size: 10px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  margin-bottom: 3px;
}

.progress-overlay__stat-value {
  font-family: var(--font-heading);
  font-size: 19px;
}

.progress-overlay__stat-suffix {
  font-size: 12px;
  color: color-mix(in srgb, var(--color-text) 45%, transparent);
}

.progress-overlay__note {
  font-size: 10.5px;
  line-height: 1.5;
  color: color-mix(in srgb, var(--color-text) 52%, transparent);
  margin: var(--space-2) 0 0;
}

.progress-overlay__footer {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin-top: var(--space-4);
}

.progress-overlay__slots {
  flex: 1;
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.progress-overlay__slot {
  font: 600 10px var(--font-heading);
  padding: 3px 6px;
  border: 1px solid color-mix(in srgb, var(--color-text) 16%, transparent);
  color: color-mix(in srgb, var(--color-text) 45%, transparent);
}

.progress-overlay__slot--done {
  background: var(--color-accent);
  color: var(--color-bg);
  border-color: var(--color-accent);
}

.progress-overlay__slot--running {
  border-color: var(--color-accent);
  color: var(--color-accent-800);
}
</style>
