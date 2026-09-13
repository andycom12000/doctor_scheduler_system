<script setup lang="ts">
/**
 * 標題列年月切換器，放在有 `:ym` 的頁面標題左側（issue #52，字級對齊
 * `docs/design-ref/screen-01.html` 的 `ymLabelS` 大字樣式）。資料來自 `GET /schedules`
 * （有值班表的月份與其狀態），是 `useResource` 的示範用法（issue #26）。下拉選單列出
 * 目前年月前後各 12 個月，加上所有有值班表的月份。
 */
import { computed, ref } from 'vue'
import { listSchedules } from '@/api/schedules'
import { shiftYearMonth, useYearMonth, yearMonthOptions } from '@/composables/useYearMonth'
import { useResource } from '@/composables/useResource'

const { ym, setYearMonth } = useYearMonth()

const listKey = ref('schedules')
const { data } = useResource(listKey, () => listSchedules())

const monthStatus = computed(() => {
  const map = new Map<string, 'draft' | 'published'>()
  for (const month of data.value?.months ?? []) map.set(month.yearMonth, month.status)
  return map
})

// 候選清單與位移邏輯跟 SCREEN 02「指定月份覆寫」的獨立月份選擇器共用，見 useYearMonth.ts。
const options = computed(() => yearMonthOptions(ym.value, [...monthStatus.value.keys()]))

function labelOf(option: string): string {
  const status = monthStatus.value.get(option)
  if (status === 'published') return `${option} · 已發布`
  if (status === 'draft') return `${option} · 草稿`
  return option
}

function shift(delta: number): void {
  setYearMonth(shiftYearMonth(ym.value, delta))
}

function onSelect(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  if (value) setYearMonth(value)
}
</script>

<template>
  <div class="ym-switcher">
    <button type="button" class="ym-switcher__step" aria-label="上一個月" @click="shift(-1)">‹</button>
    <select class="ym-switcher__select" :value="ym" aria-label="選擇年月" @change="onSelect">
      <option v-for="option in options" :key="option" :value="option">{{ labelOf(option) }}</option>
    </select>
    <button type="button" class="ym-switcher__step" aria-label="下一個月" @click="shift(1)">›</button>
  </div>
</template>

<style scoped>
.ym-switcher {
  display: flex;
  align-items: center;
  gap: 2px;
}

.ym-switcher__step {
  width: 30px;
  height: 30px;
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: 1px solid var(--color-divider);
  color: var(--color-text);
  font: 400 17px/1 var(--font-heading);
  cursor: pointer;
}

.ym-switcher__step:hover {
  background: color-mix(in srgb, var(--color-text) 7%, transparent);
}

.ym-switcher__select {
  height: 30px;
  padding: 0 var(--space-2);
  font: 600 20px/1.2 var(--font-heading);
  letter-spacing: 0.02em;
  color: var(--color-text);
  background: transparent;
  border: 1px solid transparent;
  cursor: pointer;
}

.ym-switcher__select:hover {
  border-color: var(--color-divider);
}
</style>
