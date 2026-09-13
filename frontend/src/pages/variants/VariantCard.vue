<script setup lang="ts">
/**
 * 一份變體的卡片：立場、權重乘數、熱圖、四／五個指標、空缺提示、套用／逐格差異按鈕。
 * 純視覺 + 少量本地狀態，資料轉換全部委給 heatmap.ts／variantView.ts 的純函式。
 */
import { computed } from 'vue'
import type { Variant } from '@/api/types'
import { buildHeatmap } from './heatmap'
import { buildMetricRows, formatMetricValue, formatMultipliers, formatSeconds, variantTitle } from './variantView'

const props = defineProps<{
  variant: Variant
  areaIds: string[]
  areaLabelById: Map<string, string>
  days: string[]
  groupIndexByStaff: Map<string, number>
  showFairnessPoint: boolean
  diffSelected: boolean
  /** 這份變體逐格比對目前草稿完全相同——已經是套用過的那一份（issue #54）。 */
  applied: boolean
  applying: boolean
  applyDisabled: boolean
  applyDisabledReason: string | null
  /**
   * 每個指標在「這次求解的所有變體」裡的最大值（由父層跨變體算好傳進來）。
   * 用這個當長條的分母，才能一眼看出「哪份變體空缺最多」；同一張卡片內互比不同單位的指標
   * （空缺 vs 同區延續）沒有意義，見 PR 說明。
   */
  metricMax: Record<string, number>
  /**
   * 契約沒有「每份變體的耗時」欄位，退回「整體耗時／份數」的平均值；`null` 時不顯示
   * （PR 說明列出這個近似）。
   */
  perVariantSeconds: number | null
}>()

const emit = defineEmits<{ apply: []; toggleDiff: [] }>()

const title = computed(() => variantTitle(props.variant.id))
const multipliers = computed(() => formatMultipliers(props.variant.weightProfile))
const metricRows = computed(() => buildMetricRows(props.variant.metrics, props.showFairnessPoint))
const heatmap = computed(() => buildHeatmap(props.variant.duties, props.areaIds, props.days, props.groupIndexByStaff))
const vacancies = computed(() => props.variant.metrics?.vacancies ?? 0)
</script>

<template>
  <article class="variant-card" :class="{ 'variant-card--selected': diffSelected, 'variant-card--applied': applied }">
    <header class="variant-card__head">
      <h3 class="variant-card__title">{{ title }}</h3>
      <span class="tag tag-neutral" :title="variant.description ?? undefined">{{ variant.label }}</span>
      <span class="variant-card__secs">{{ formatSeconds(perVariantSeconds) }}</span>
    </header>

    <div class="variant-card__mults">
      <!-- 平衡變體（乘數全部是 1）formatMultipliers 回空陣列——顯示一顆「全部 ×1」晶片
           而不是整排消失，跟 stanceLine 04b 的說法一致（canvas-data.js:788 同樣是
           mults: ['全部 ×1']，協調者審查回饋）。 -->
      <template v-if="multipliers.length">
        <span v-for="m in multipliers" :key="m.code" class="tag tag-neutral">{{ m.text }}</span>
      </template>
      <span v-else class="tag tag-neutral">全部 ×1</span>
    </div>

    <p v-if="variant.hardViolationCount > 0" class="variant-card__hard-warning">
      硬違規 {{ variant.hardViolationCount }} 項——理論上求解器不該產出，回報時附上這份變體。
    </p>

    <div class="variant-card__section-label">指派熱圖 · {{ areaIds.length }} 區 × {{ days.length }} 日（色階＝身分組）</div>
    <div class="variant-card__heatmap" :style="{ gridTemplateColumns: `repeat(${days.length}, 1fr)` }">
      <template v-for="(row, rowIndex) in heatmap" :key="areaIds[rowIndex] ?? rowIndex">
        <div
          v-for="cell in row"
          :key="`${cell.areaId}:${cell.date}`"
          class="variant-card__cell"
          :class="{ 'variant-card__cell--vacant': cell.staffId === null }"
          :style="
            cell.staffId !== null && cell.groupIndex !== null
              ? { background: `var(--group-${Math.min(cell.groupIndex + 1, 4)})` }
              : undefined
          "
          :title="`${areaLabelById.get(cell.areaId) ?? cell.areaId} · ${cell.date}`"
        />
      </template>
    </div>

    <div class="variant-card__section-label">指標比較 · 跨這批變體同一指標互比，長條越短越好</div>
    <dl class="variant-card__metrics">
      <div v-for="row in metricRows" :key="row.key" class="variant-card__metric-row">
        <dt>{{ row.label }}</dt>
        <div class="variant-card__metric-bar">
          <div
            class="variant-card__metric-fill"
            :style="{ width: `${Math.min(100, (Math.abs(row.value) / Math.max(1, metricMax[row.key] ?? 0)) * 100)}%` }"
          />
        </div>
        <dd>{{ formatMetricValue(row.key, row.value) }}</dd>
      </div>
    </dl>

    <p v-if="vacancies > 0" class="variant-card__vacancy-note variant-card__vacancy-note--warn">
      空缺 {{ vacancies }} 格：登記過多時的正常結果，求解器回「空缺最少」的變體，而不是整份無解。
    </p>
    <p v-else class="variant-card__vacancy-note">無空缺：{{ areaIds.length }} 區 × {{ days.length }} 日全數填補。</p>

    <div class="variant-card__actions">
      <button
        type="button"
        class="btn"
        :class="applied ? 'btn-primary' : 'btn-secondary'"
        :disabled="applyDisabled || applying"
        :title="applyDisabledReason ?? undefined"
        @click="emit('apply')"
      >
        {{ applying ? '套用中…' : applied ? '✓ 已選定' : '選定此變體' }}
      </button>
      <button
        type="button"
        class="btn btn-secondary"
        :class="{ 'btn-secondary--active': diffSelected }"
        @click="emit('toggleDiff')"
      >
        逐格差異
      </button>
    </div>
    <p v-if="applyDisabledReason" class="variant-card__apply-note">{{ applyDisabledReason }}</p>
  </article>
</template>

<style scoped>
.variant-card {
  flex: 1;
  min-width: 260px;
  display: flex;
  flex-direction: column;
  padding: var(--space-4);
  border: 1px solid var(--color-divider);
  background: var(--color-bg);
}

.variant-card--selected {
  border-color: var(--color-accent);
  background: color-mix(in srgb, var(--color-accent) 6%, transparent);
}

/* 已套用為草稿的那一份：比逐格差異的高亮更強調，兩個狀態可以同時成立。 */
.variant-card--applied {
  border-width: 2px;
  border-color: var(--color-accent);
  box-shadow: inset 0 0 0 1px var(--color-accent);
}

.variant-card__head {
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
}

.variant-card__title {
  margin: 0;
  font-size: 20px;
}

.variant-card__secs {
  margin-left: auto;
  font: 600 11px var(--font-heading);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.variant-card__mults {
  display: flex;
  gap: 6px;
  margin-top: var(--space-2);
  flex-wrap: wrap;
}

.variant-card__hard-warning {
  margin: var(--space-2) 0 0;
  padding: 6px 8px;
  font-size: 11.5px;
  border: 1px solid var(--color-accent-900);
  color: var(--color-accent-900);
  background: color-mix(in srgb, var(--color-accent-900) 8%, transparent);
}

.variant-card__section-label {
  margin-top: var(--space-3);
  font-family: var(--font-heading);
  font-size: 10px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.variant-card__heatmap {
  display: grid;
  gap: 1px;
  margin-top: 5px;
  height: 56px;
  background: var(--color-divider);
}

.variant-card__cell {
  background: var(--color-neutral-200);
}

.variant-card__cell--vacant {
  background: var(--cell-vacancy-bg);
  box-shadow: inset 0 0 0 1px var(--cell-vacancy-outline);
}

.variant-card__metrics {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin: var(--space-3) 0 0;
}

.variant-card__metric-row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: 11.5px;
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
  padding-bottom: 3px;
}

.variant-card__metric-row dt {
  width: 92px;
  flex: none;
  color: color-mix(in srgb, var(--color-text) 58%, transparent);
}

.variant-card__metric-bar {
  flex: 1;
  height: 4px;
  background: color-mix(in srgb, var(--color-text) 9%, transparent);
}

.variant-card__metric-fill {
  height: 100%;
  background: var(--color-accent);
}

.variant-card__metric-row dd {
  margin: 0;
  width: 64px;
  flex: none;
  text-align: right;
  font-weight: 600;
}

.variant-card__vacancy-note {
  margin: var(--space-2) 0 0;
  padding: 6px 8px;
  font-size: 10.5px;
  line-height: 1.5;
  border: 1px solid color-mix(in srgb, var(--color-text) 12%, transparent);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.variant-card__vacancy-note--warn {
  border-color: var(--color-accent);
  color: var(--color-accent-900);
  background: color-mix(in srgb, var(--color-accent) 8%, transparent);
}

.variant-card__actions {
  display: flex;
  gap: 6px;
  margin-top: var(--space-3);
}

.btn-secondary--active {
  background: var(--color-accent);
  color: var(--color-bg);
  border-color: var(--color-accent);
}

.variant-card__apply-note {
  margin: 4px 0 0;
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}
</style>
