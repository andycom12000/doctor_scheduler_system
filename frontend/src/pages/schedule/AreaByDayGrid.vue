<script setup lang="ts">
/**
 * 區域 × 日：列＝5 區（依區域類型分組顯示，比照設計稿的一般病房／ICU／總值三段），
 * 欄＝天。格內姓名簡稱；假日欄底色、值班格底色（平日／假日）、空缺／違規疊上
 * `violationStyle.ts` 算出的渲染種類。
 */
import { computed } from 'vue'
import type { Area, AreaType } from '@/api/types'
import { abbreviate, areaCellKey, type DayColumn, type StaffDirectoryEntry } from './lib/scheduleGrid'
import type { CellRenderKind } from './lib/violationStyle'
import { domIdForCellKey } from './lib/cellNav'

const props = defineProps<{
  areaTypes: AreaType[]
  areas: Area[]
  days: DayColumn[]
  dutyMap: Map<string, string>
  staffDirectory: Map<string, StaffDirectoryEntry>
  cellRenderIndex: Map<string, CellRenderKind>
  vacancyCounts: Map<string, number>
}>()

const emit = defineEmits<{ cellClick: [areaId: string, date: string] }>()

interface GroupedAreas {
  areaType: AreaType
  areas: Area[]
}

const groups = computed<GroupedAreas[]>(() =>
  props.areaTypes
    .map((areaType) => ({ areaType, areas: props.areas.filter((area) => area.areaTypeCode === areaType.code) }))
    .filter((group) => group.areas.length > 0),
)

function staffIdAt(areaId: string, date: string): string | undefined {
  return props.dutyMap.get(`${areaId}|${date}`)
}

function directoryOf(staffId: string | undefined): StaffDirectoryEntry | undefined {
  return staffId ? props.staffDirectory.get(staffId) : undefined
}

function cellClass(areaId: string, date: string, isHoliday: boolean): Record<string, boolean> {
  const staffId = staffIdAt(areaId, date)
  const render = props.cellRenderIndex.get(areaCellKey(areaId, date))
  return {
    'ad-grid__cell--duty': Boolean(staffId) && render !== 'violation-stripe' && render !== 'violation-bg',
    'ad-grid__cell--holiday': isHoliday && Boolean(staffId),
    'ad-grid__cell--vacancy': render === 'vacancy',
    'ad-grid__cell--violation-stripe': render === 'violation-stripe',
    'ad-grid__cell--violation-bg': render === 'violation-bg',
  }
}

function filled(areaId: string): number {
  return props.days.filter((day) => staffIdAt(areaId, day.date)).length
}
</script>

<template>
  <div class="area-by-day">
    <div class="ad-grid" :style="{ gridTemplateColumns: `132px repeat(${days.length}, 26px) 44px` }">
      <div class="ad-grid__corner">區域 / 日期</div>
      <div v-for="day in days" :key="day.date" class="ad-grid__head" :class="{ 'ad-grid__head--holiday': day.isHoliday }">
        <span class="ad-grid__wd">{{ day.weekday }}</span>
        <span class="ad-grid__dd">{{ day.dd }}</span>
        <span class="ad-grid__pt">{{ day.quotaPointValue }}</span>
      </div>
      <div class="ad-grid__corner">填補</div>

      <template v-for="group in groups" :key="group.areaType.code">
        <div class="ad-grid__group" :style="{ gridColumn: `1 / span ${days.length + 2}` }">
          <span class="tag tag-accent">{{ group.areaType.code }}</span>
          <span class="ad-grid__group-name">{{ group.areaType.name }}</span>
        </div>

        <template v-for="area in group.areas" :key="area.id">
          <div class="ad-grid__label">{{ area.name }}</div>
          <button
            v-for="day in days"
            :id="domIdForCellKey(areaCellKey(area.id, day.date)) ?? undefined"
            :key="day.date"
            type="button"
            class="ad-grid__cell"
            :class="cellClass(area.id, day.date, day.isHoliday)"
            @click="emit('cellClick', area.id, day.date)"
          >
            {{ directoryOf(staffIdAt(area.id, day.date))?.name ? abbreviate(directoryOf(staffIdAt(area.id, day.date))!.name) : '' }}
          </button>
          <div class="ad-grid__stat">{{ filled(area.id) }}/{{ days.length }}</div>
        </template>
      </template>

      <div class="ad-grid__label ad-grid__label--foot">每日已填補</div>
      <div v-for="day in days" :key="day.date" class="ad-grid__fill">
        {{ areas.length - (vacancyCounts.get(day.date) ?? 0) }}
      </div>
      <div class="ad-grid__fill"></div>
    </div>

    <div class="ad-legend">
      <span class="k">圖例</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--duty" />平日值班（格內為姓名簡稱）</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--duty-holiday" />假日值班</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--blocked" />不可排班日（求解輸入，見不可排班日登記）</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--holiday" />假日（整列底色）</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--vacancy" />該日有區域空缺（H1）</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--violation" />排到已登記的不可排班日（H5）</span>
    </div>
  </div>
</template>

<style scoped>
.area-by-day {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-width: 0;
}

.ad-grid {
  display: grid;
  align-content: start;
  overflow: auto;
  border-top: 1px solid var(--color-divider);
  border-left: 1px solid var(--color-divider);
}

.ad-grid__corner,
.ad-grid__head,
.ad-grid__label,
.ad-grid__cell,
.ad-grid__stat,
.ad-grid__fill {
  border-right: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
}

.ad-grid__corner {
  position: sticky;
  left: 0;
  top: 0;
  z-index: 2;
  background: var(--color-bg);
  display: flex;
  align-items: center;
  padding-left: var(--space-2);
  font: 600 10px var(--font-heading);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.ad-grid__head {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--color-bg);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3px 0;
}

.ad-grid__head--holiday {
  background: var(--cell-holiday-bg);
}

.ad-grid__wd {
  font-size: 8.5px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.ad-grid__dd {
  font: 600 11px var(--font-heading);
}

.ad-grid__pt {
  font: 600 8px ui-monospace, Menlo, monospace;
  color: color-mix(in srgb, var(--color-text) 40%, transparent);
}

.ad-grid__group {
  position: sticky;
  left: 0;
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: 6px var(--space-2);
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-divider);
}

.ad-grid__group-name {
  font: 600 12px var(--font-heading);
}

.ad-grid__label {
  position: sticky;
  left: 0;
  background: var(--color-bg);
  display: flex;
  align-items: center;
  padding-left: var(--space-2);
  font: 600 11.5px var(--font-heading);
  letter-spacing: 0.04em;
}

.ad-grid__label--foot {
  border-top: 1px solid var(--color-divider);
}

.ad-grid__cell {
  height: 24px;
  padding: 0;
  background: transparent;
  border-top: none;
  border-left: none;
  cursor: pointer;
  font: 500 9.5px 'Microsoft JhengHei UI', sans-serif;
  color: var(--color-accent-900);
}

.ad-grid__cell:hover {
  outline: 1px solid var(--color-accent);
  outline-offset: -1px;
}

.ad-grid__cell--duty {
  background: var(--cell-duty-bg);
}

.ad-grid__cell--duty.ad-grid__cell--holiday {
  background: var(--cell-duty-holiday-bg);
}

.ad-grid__cell--vacancy {
  background: var(--cell-vacancy-bg);
  box-shadow: inset 0 0 0 1.5px var(--cell-vacancy-outline);
}

.ad-grid__cell--violation-stripe {
  background: var(--cell-violation-stripe);
  box-shadow: inset 0 0 0 1.5px var(--cell-violation-outline);
  color: var(--color-bg);
}

.ad-grid__cell--violation-bg {
  background: var(--cell-violation-bg);
  color: var(--color-bg);
}

.ad-grid__stat,
.ad-grid__fill {
  display: flex;
  align-items: center;
  justify-content: center;
  font: 600 10px ui-monospace, Menlo, monospace;
  color: color-mix(in srgb, var(--color-text) 45%, transparent);
}

.ad-legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
  align-items: center;
  font-size: 11px;
}

.ad-legend__item {
  display: flex;
  align-items: center;
  gap: 5px;
}

.ad-legend__swatch {
  width: 22px;
  height: 15px;
  border: 1px solid color-mix(in srgb, var(--color-text) 10%, transparent);
}

.ad-legend__swatch--duty {
  background: var(--cell-duty-bg);
}

.ad-legend__swatch--duty-holiday {
  background: var(--cell-duty-holiday-bg);
}

.ad-legend__swatch--blocked {
  background: var(--cell-blocked-bg);
}

.ad-legend__swatch--holiday {
  background: var(--cell-holiday-bg);
}

.ad-legend__swatch--vacancy {
  background: var(--cell-vacancy-bg);
}

.ad-legend__swatch--violation {
  background: var(--cell-violation-stripe);
}
</style>
