<script setup lang="ts">
/** 身分組容量利用率：可行性指標，組內 Σ已排 ÷ Σ上限，就地由點數看板算（`lib/pointBoardStats.ts`）。 */
import { computed } from 'vue'
import type { PointBoardGroup } from '@/api/types'
import { computeGroupUtilization } from './lib/pointBoardStats'

const props = defineProps<{ groups: PointBoardGroup[] }>()

const utilization = computed(() => computeGroupUtilization(props.groups))
</script>

<template>
  <section class="utilization">
    <div class="utilization__title">
      <span class="k">身分組容量利用率</span>
      <span class="tag tag-outline">可行性指標</span>
    </div>
    <div v-for="row in utilization" :key="row.groupCode" class="utilization__row">
      <div class="utilization__line">
        <span class="utilization__code">{{ row.groupCode }}</span>
        <span class="utilization__text">
          {{ row.capPoints === null ? '不計額度' : `${row.usedPoints} / ${row.capPoints} 點` }}
        </span>
        <span class="utilization__pct">{{ row.percent === null ? '—' : `${row.percent}%` }}</span>
      </div>
      <div class="utilization__track">
        <div
          v-if="row.percent !== null"
          class="utilization__bar"
          :class="{ 'utilization__bar--hot': row.percent >= 92 }"
          :style="{ width: `${row.percent}%` }"
        />
      </div>
    </div>
  </section>
</template>

<style scoped>
.utilization {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.utilization__title {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

/* `.tag-outline` 不在 styles.css 的最小共用元件類別裡（只有 tag-neutral／tag-accent），
   這裡用 scoped 樣式補一個純外框的變化，不動全域檔案。 */
.utilization__title .tag-outline {
  background: transparent;
  border: 1px solid color-mix(in srgb, var(--color-text) 20%, transparent);
  color: color-mix(in srgb, var(--color-text) 60%, transparent);
}

.utilization__row {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.utilization__line {
  display: flex;
  align-items: baseline;
  gap: 6px;
  font-size: 11px;
}

.utilization__code {
  font: 600 11px var(--font-heading);
}

.utilization__text {
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.utilization__pct {
  margin-left: auto;
  font: 600 12px var(--font-heading);
}

.utilization__track {
  height: 4px;
  background: color-mix(in srgb, var(--color-text) 9%, transparent);
}

.utilization__bar {
  height: 4px;
  background: var(--color-accent);
}

.utilization__bar--hot {
  background: var(--color-accent-800);
}
</style>
