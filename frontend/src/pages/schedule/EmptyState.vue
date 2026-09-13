<script setup lang="ts">
/**
 * 空月份：`GET /schedules/{ym}` 404。用 `/settings/areas` 與行事曆畫出空的 5 × N 格線，
 * 副標「值班表尚未產生」，工具列給「登記不可排班日」與「求解」兩個連結。
 *
 * 格子仍然可以點——`openCell` 會先用 `PATCH duties`（`staffId: null`）讓後端自動建一份
 * 空草稿，畫面收到 `MutationResult` 後 `invalidate('schedules')` 切回正常狀態
 * （見 index.vue 的 `openCell`，這裡只負責畫格線與轉發點擊）。
 */
import type { Area } from '@/api/types'
import type { DayColumn } from './lib/scheduleGrid'

defineProps<{
  areas: Area[]
  days: DayColumn[]
  ym: string
  priming: boolean
  primeError: string | null
}>()

const emit = defineEmits<{
  cellClick: [areaId: string, date: string]
  registerBlockedDays: []
  solve: []
}>()
</script>

<template>
  <div class="empty-state">
    <div class="empty-state__banner">
      <div>
        <div class="empty-state__title">值班表尚未產生</div>
        <div class="empty-state__subtitle">{{ ym }}：點一格即可從空白開始手排，後端會自動建立草稿。</div>
      </div>
      <div class="empty-state__actions">
        <button type="button" class="btn btn-secondary" @click="emit('registerBlockedDays')">登記不可排班日</button>
        <button type="button" class="btn btn-primary" @click="emit('solve')">求解</button>
      </div>
    </div>

    <p v-if="primeError" class="empty-state__error">{{ primeError }}</p>

    <div class="empty-grid" :style="{ gridTemplateColumns: `132px repeat(${days.length}, 26px)` }">
      <div class="empty-grid__corner">區域 / 日期</div>
      <div v-for="day in days" :key="day.date" class="empty-grid__head" :class="{ 'empty-grid__head--holiday': day.isHoliday }">
        <span class="empty-grid__wd">{{ day.weekday }}</span>
        <span class="empty-grid__dd">{{ day.dd }}</span>
      </div>

      <template v-for="area in areas" :key="area.id">
        <div class="empty-grid__label">{{ area.name }}</div>
        <button
          v-for="day in days"
          :key="day.date"
          type="button"
          class="empty-grid__cell"
          :class="{ 'empty-grid__cell--holiday': day.isHoliday, 'empty-grid__cell--busy': priming }"
          :disabled="priming"
          :aria-label="`${area.name} ${day.date}`"
          @click="emit('cellClick', area.id, day.date)"
        />
      </template>
    </div>
  </div>
</template>

<style scoped>
.empty-state {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  height: 100%;
  min-height: 0;
}

.empty-state__banner {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  padding: var(--space-3) var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-divider);
}

.empty-state__title {
  font-family: var(--font-heading);
  font-weight: 600;
  font-size: 16px;
}

.empty-state__subtitle {
  margin-top: 2px;
  font-size: 12px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.empty-state__actions {
  margin-left: auto;
  display: flex;
  gap: var(--space-2);
  flex: none;
}

.empty-state__error {
  margin: 0;
  padding: var(--space-2) var(--space-3);
  font-size: 12px;
  background: var(--color-accent-100);
  color: var(--color-accent-900);
  border: 1px solid var(--color-accent);
}

.empty-grid {
  display: grid;
  align-content: start;
  overflow: auto;
  border-top: 1px solid var(--color-divider);
  border-left: 1px solid var(--color-divider);
}

.empty-grid__corner,
.empty-grid__head,
.empty-grid__label,
.empty-grid__cell {
  border-right: 1px solid color-mix(in srgb, var(--color-text) 10%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 10%, transparent);
}

.empty-grid__corner {
  position: sticky;
  left: 0;
  top: 0;
  z-index: 2;
  background: var(--color-bg);
  font: 600 10px var(--font-heading);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  display: flex;
  align-items: center;
  padding-left: var(--space-2);
}

.empty-grid__head {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--color-bg);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4px 0;
}

.empty-grid__head--holiday {
  background: var(--cell-holiday-bg);
}

.empty-grid__wd {
  font-size: 8.5px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.empty-grid__dd {
  font: 600 11px var(--font-heading);
}

.empty-grid__label {
  position: sticky;
  left: 0;
  background: var(--color-bg);
  display: flex;
  align-items: center;
  padding-left: var(--space-2);
  font: 600 12px var(--font-heading);
}

.empty-grid__cell {
  height: 24px;
  padding: 0;
  background: transparent;
  border-top: none;
  border-left: none;
  cursor: pointer;
}

.empty-grid__cell:hover:not(:disabled) {
  background: color-mix(in srgb, var(--color-accent) 12%, transparent);
}

.empty-grid__cell--holiday {
  background: var(--cell-holiday-bg);
}

.empty-grid__cell--busy {
  cursor: wait;
  opacity: 0.6;
}
</style>
