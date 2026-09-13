<script setup lang="ts">
/**
 * 單日詳表：`days/{date}` 自帶月負載，不需要另打點數看板。前一日／次一日翻頁，
 * 點一列開候選人面板（沿用區域 × 日、日 × 人共用的同一支 `openCell`）。
 */
import type { DayDetail } from '@/api/types'

const props = defineProps<{
  date: string
  detail: DayDetail | null
  loading: boolean
  errorMessage: string | null
  canPrev: boolean
  canNext: boolean
  areaNames: Map<string, string>
  areaTypeNames: Map<string, string>
}>()

const emit = defineEmits<{ prev: []; next: []; cellClick: [areaId: string, date: string] }>()

function areaLabel(areaId: string, fallbackCode: string): string {
  return props.areaNames.get(areaId) ?? fallbackCode
}
function areaTypeLabel(code: string): string {
  return props.areaTypeNames.get(code) ?? code
}
</script>

<template>
  <div class="day-detail">
    <div class="day-detail__nav">
      <button type="button" class="btn btn-secondary" :disabled="!canPrev" @click="emit('prev')">前一日</button>
      <div class="day-detail__heading">
        <div class="day-detail__date">{{ date }}</div>
        <div v-if="detail" class="day-detail__sub">
          {{ detail.isHoliday ? '假日' : '平日' }} · 1 班 {{ detail.quotaPointValue }} 點 · {{ detail.areas.length }} 區各 1 人
        </div>
      </div>
      <button type="button" class="btn btn-secondary" :disabled="!canNext" @click="emit('next')">次一日</button>
      <span v-if="detail" class="tag" :class="detail.areas.some((a) => !a.filled) ? 'tag-accent' : 'tag-outline'">
        {{ detail.areas.filter((a) => !a.filled).length ? `${detail.areas.filter((a) => !a.filled).length} 區空缺` : '全數填補' }}
      </span>
    </div>

    <div v-if="loading" class="day-detail__state">載入中…</div>
    <div v-else-if="errorMessage" class="day-detail__state">{{ errorMessage }}</div>
    <div v-else-if="detail" class="day-detail__rows">
      <button
        v-for="area in detail.areas"
        :key="area.areaId"
        type="button"
        class="day-detail__row"
        :class="{ 'day-detail__row--vacant': !area.filled }"
        @click="emit('cellClick', area.areaId, detail.date)"
      >
        <span class="day-detail__area">{{ areaLabel(area.areaId, area.code) }}</span>
        <span class="day-detail__type">{{ areaTypeLabel(area.areaTypeCode) }}</span>
        <span class="day-detail__rank">{{ area.staff?.rankCode ?? '—' }}</span>
        <span class="day-detail__name">{{ area.staff?.name ?? '空缺' }}</span>
        <span class="day-detail__load">
          <template v-if="area.staff">
            額度 {{ area.staff.monthQuotaPoints }}/{{ area.staff.monthQuotaCap ?? '不計' }} · 假日 {{ area.staff.monthHolidayDuties }} 班
          </template>
          <template v-else>無合格且可用的人</template>
        </span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.day-detail {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.day-detail__nav {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
}

.day-detail__heading {
  min-width: 160px;
}

.day-detail__date {
  font: 600 20px var(--font-heading);
  letter-spacing: 0.03em;
}

.day-detail__sub {
  font-size: 11.5px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

/* `.tag-outline` 不在 styles.css 的最小共用元件類別裡（只有 tag-neutral／tag-accent），
   三個用到它的檔案（這裡、UtilizationPanel、ViolationSidebar）統一補這個 Industry 原版。 */
.tag-outline {
  background: transparent;
  border: 1px solid var(--color-accent);
  color: var(--color-accent);
}

.day-detail__state {
  padding: var(--space-4);
  font-size: 12px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.day-detail__rows {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  max-width: 760px;
}

.day-detail__row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--color-divider);
  background: var(--color-bg);
  cursor: pointer;
  text-align: left;
  font: inherit;
  color: inherit;
}

.day-detail__row--vacant {
  border-color: var(--color-accent);
  background: var(--cell-vacancy-bg);
}

.day-detail__area {
  flex: none;
  width: 46px;
  font: 600 13px var(--font-heading);
  letter-spacing: 0.06em;
}

.day-detail__type {
  flex: none;
  width: 96px;
  font-size: 11.5px;
  color: color-mix(in srgb, var(--color-text) 62%, transparent);
}

.day-detail__rank {
  flex: none;
  width: 42px;
  text-align: center;
  font: 600 9.5px var(--font-heading);
  padding: 2px 4px;
  background: var(--color-accent-100);
  color: var(--color-accent-800);
}

.day-detail__name {
  flex: none;
  width: 70px;
  font-size: 12.5px;
}

.day-detail__load {
  margin-left: auto;
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 52%, transparent);
  text-align: right;
}
</style>
