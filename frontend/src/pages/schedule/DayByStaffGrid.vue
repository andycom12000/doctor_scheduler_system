<script setup lang="ts">
/**
 * 日 × 人：列＝天、欄＝在職人員依 4 身分組分區，右側「空缺」欄。
 * 欄很多，欄寬窄、表頭旋轉；第一欄（日期）與表頭固定（`position: sticky`）。
 *
 * `staff:{staffId}:{date}` cellKey 的違規（值休休、額度超標等）疊在對應欄位上。
 */
import { computed } from 'vue'
import type { Area, PointBoardGroup, VacancyByDate } from '@/api/types'
import type { DayColumn } from './lib/scheduleGrid'
import { staffCellKey } from './lib/scheduleGrid'
import type { CellRenderKind } from './lib/violationStyle'
import { domIdForCellKey } from './lib/cellNav'

const props = defineProps<{
  areas: Area[]
  days: DayColumn[]
  pointBoardGroups: PointBoardGroup[]
  dutiesByStaff: Map<string, string>
  cellRenderIndex: Map<string, CellRenderKind>
  vacancyByDate: VacancyByDate[]
}>()

const emit = defineEmits<{ cellClick: [areaId: string, date: string] }>()

const areaCodeById = computed(() => new Map(props.areas.map((area) => [area.id, area.name])))
const vacancyByDateMap = computed(() => new Map(props.vacancyByDate.map((entry) => [entry.date, entry])))

function areaAt(staffId: string, date: string): string | undefined {
  return props.dutiesByStaff.get(`${staffId}|${date}`)
}

function cellClass(staffId: string, date: string): Record<string, boolean> {
  const areaId = areaAt(staffId, date)
  const render = props.cellRenderIndex.get(staffCellKey(staffId, date))
  return {
    'dp-grid__cell--duty': Boolean(areaId) && !render,
    'dp-grid__cell--violation-bg': render === 'violation-bg',
    'dp-grid__cell--violation-stripe': render === 'violation-stripe',
  }
}

function cellClick(staffId: string, date: string): void {
  const areaId = areaAt(staffId, date)
  if (areaId) emit('cellClick', areaId, date)
}
</script>

<template>
  <div class="day-by-staff">
    <div
      class="dp-grid"
      :style="{ gridTemplateColumns: `44px repeat(${pointBoardGroups.reduce((n, g) => n + g.rows.length, 0)}, 23px) 56px` }"
    >
      <div class="dp-grid__corner"></div>
      <template v-for="group in pointBoardGroups" :key="group.groupCode">
        <div class="dp-grid__group" :style="{ gridColumn: `span ${group.rows.length}` }">
          {{ group.groupCode }} · {{ group.rows.length }} 人
        </div>
      </template>
      <div class="dp-grid__corner"></div>

      <div class="dp-grid__corner dp-grid__corner--label">日期</div>
      <template v-for="group in pointBoardGroups" :key="`${group.groupCode}-head`">
        <div v-for="row in group.rows" :key="row.staffId" class="dp-grid__head" :title="`${row.name}（${row.rankCode}）`">
          <span class="dp-grid__head-name">{{ row.name }}</span>
          <span class="dp-grid__head-rank">{{ row.rankCode }}</span>
        </div>
      </template>
      <div class="dp-grid__corner dp-grid__corner--label">未填補</div>

      <template v-for="day in days" :key="day.date">
        <div class="dp-grid__label" :class="{ 'dp-grid__label--holiday': day.isHoliday }">
          {{ day.dd }}<span class="dp-grid__wd">{{ day.weekday }}</span>
        </div>
        <template v-for="group in pointBoardGroups" :key="`${group.groupCode}-${day.date}`">
          <button
            v-for="row in group.rows"
            :id="domIdForCellKey(staffCellKey(row.staffId, day.date)) ?? undefined"
            :key="row.staffId"
            type="button"
            class="dp-grid__cell"
            :class="cellClass(row.staffId, day.date)"
            @click="cellClick(row.staffId, day.date)"
          >
            {{ areaAt(row.staffId, day.date) ? areaCodeById.get(areaAt(row.staffId, day.date)!) : '' }}
          </button>
        </template>
        <div class="dp-grid__vac" :class="{ 'dp-grid__vac--some': (vacancyByDateMap.get(day.date)?.count ?? 0) > 0 }">
          {{ vacancyByDateMap.get(day.date)?.areaIds.map((id) => areaCodeById.get(id)).join(' ') ?? '' }}
        </div>
      </template>
    </div>

    <div class="ad-legend">
      <span class="k">身分組</span>
      <span v-for="group in pointBoardGroups" :key="group.groupCode" class="ad-legend__item">
        <span class="ad-legend__swatch ad-legend__swatch--duty" />{{ group.groupName }}
      </span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch ad-legend__swatch--vacancy" />空缺（H1 · 登記過多時的正常結果）
      </span>
      <span class="dp-note">格內為區域代號 · 空白＝未值班 · 不可排班日與拖拉對調見 #34／#29</span>
    </div>
  </div>
</template>

<style scoped>
.day-by-staff {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-width: 0;
}

.dp-grid {
  display: grid;
  align-content: start;
  overflow: auto;
  border-top: 1px solid var(--color-divider);
  border-left: 1px solid var(--color-divider);
  max-height: 480px;
}

.dp-grid__corner,
.dp-grid__group,
.dp-grid__head,
.dp-grid__label,
.dp-grid__cell,
.dp-grid__vac {
  border-right: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
}

.dp-grid__corner {
  position: sticky;
  left: 0;
  top: 0;
  z-index: 3;
  background: var(--color-bg);
}

.dp-grid__corner--label {
  top: 20px;
  font: 600 10px var(--font-heading);
  display: flex;
  align-items: center;
  justify-content: center;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.dp-grid__group {
  position: sticky;
  top: 0;
  z-index: 2;
  background: var(--color-accent-100);
  color: var(--color-accent-800);
  font: 600 10px var(--font-heading);
  letter-spacing: 0.06em;
  text-align: center;
  padding: 4px 0;
}

.dp-grid__head {
  position: sticky;
  top: 20px;
  z-index: 1;
  background: var(--color-bg);
  height: 86px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  overflow: hidden;
  padding: 4px 0;
}

.dp-grid__head-name {
  writing-mode: vertical-rl;
  text-orientation: upright;
  font: 500 10.5px 'Microsoft JhengHei UI', sans-serif;
  letter-spacing: 0.04em;
}

.dp-grid__head-rank {
  flex: none;
  font: 600 9px var(--font-heading);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.dp-grid__label {
  position: sticky;
  left: 0;
  background: var(--color-bg);
  display: flex;
  align-items: center;
  gap: 3px;
  padding-left: 4px;
  font: 600 10.5px var(--font-heading);
}

.dp-grid__label--holiday {
  background: var(--cell-holiday-bg);
}

.dp-grid__wd {
  font-size: 8px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.dp-grid__cell {
  height: 21px;
  padding: 0;
  border-top: none;
  border-left: none;
  background: transparent;
  cursor: pointer;
  font: 600 9px ui-monospace, Menlo, monospace;
  color: var(--color-accent-900);
}

.dp-grid__cell:hover {
  outline: 1px solid var(--color-accent);
  outline-offset: -1px;
}

.dp-grid__cell--duty {
  background: var(--cell-duty-bg);
}

.dp-grid__cell--violation-bg {
  background: var(--cell-violation-bg);
  color: var(--color-bg);
}

.dp-grid__cell--violation-stripe {
  background: var(--cell-violation-stripe);
  box-shadow: inset 0 0 0 1.5px var(--cell-violation-outline);
  color: var(--color-bg);
}

.dp-grid__vac {
  display: flex;
  align-items: center;
  justify-content: center;
  font: 600 9px ui-monospace, Menlo, monospace;
  color: color-mix(in srgb, var(--color-text) 35%, transparent);
}

.dp-grid__vac--some {
  background: var(--cell-vacancy-bg);
  color: var(--color-accent-900);
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
  width: 20px;
  height: 14px;
  border: 1px solid color-mix(in srgb, var(--color-text) 10%, transparent);
}

.ad-legend__swatch--duty {
  background: var(--cell-duty-bg);
}

.ad-legend__swatch--vacancy {
  background: var(--cell-vacancy-bg);
}

.dp-note {
  font-size: 11px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}
</style>
