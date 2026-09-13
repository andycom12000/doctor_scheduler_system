<script setup lang="ts">
/**
 * 筆刷式矩陣本體：列＝在職人員依身分組分區，欄＝該月每一天，右側「登」「餘」兩欄，
 * 底部「該日登記人數」「總值可用人數」兩列。用一個 CSS Grid 容器＋自動排列（見 scoped style），
 * 群組標題列用 `grid-column: 1 / -1` 撐滿整行，不用手算 grid-row/grid-column 索引。
 *
 * 拖曳塗格用 `usePointerPaint`（issue #29）：每一格的 `pointerdown` 交給它，
 * 容器的 `pointermove`／`pointerup`／`pointercancel` 也交給它；解析出格子鍵後
 * 換算回 `(staffId, date)` 往上 emit，寫入與 409 由 `index.vue` 處理。
 */
import { computed } from 'vue'
import type { CalendarDay } from '@/api/types'
import type { BlockedDayGroupView } from './logic'
import { usePointerPaint } from './usePointerPaint'

const props = defineProps<{
  groups: BlockedDayGroupView[]
  days: CalendarDay[]
  footerCounts: Map<string, number>
  chiefAvailability: Map<string, number>
  monthlyCap: number
}>()

const emit = defineEmits<{
  paint: [staffId: string, date: string]
  strokeEnd: []
}>()

const WEEKDAY_LABELS = ['日', '一', '二', '三', '四', '五', '六']

/**
 * 直接算出完整的 `grid-template-columns` 字串，不靠 `repeat(var(--n), …)`——
 * 用 CSS 變數當 `repeat()` 的重複次數在部分 Chromium 版本上不可靠，
 * 字串內插保證 WebView2 152 一定吃得到正確的欄數。
 */
const gridTemplateColumns = computed(() => `148px repeat(${props.days.length}, 23px) 34px 34px`)

function weekdayLabel(day: CalendarDay): string {
  return WEEKDAY_LABELS[day.weekday] ?? ''
}

function dayNumber(day: CalendarDay): string {
  return day.date.slice(-2)
}

function paintKey(staffId: string, date: string): string {
  return `${staffId}|${date}`
}

const pointerPaint = usePointerPaint({
  onPaint: (key) => {
    const separatorIndex = key.indexOf('|')
    if (separatorIndex < 0) return
    emit('paint', key.slice(0, separatorIndex), key.slice(separatorIndex + 1))
  },
})

function onCellPointerDown(event: PointerEvent, staffId: string, date: string): void {
  pointerPaint.onCellPointerDown(event, paintKey(staffId, date))
}

function onContainerPointerUp(event: PointerEvent): void {
  pointerPaint.onPointerUp(event)
  emit('strokeEnd')
}

function onContainerPointerCancel(event: PointerEvent): void {
  pointerPaint.onPointerCancel(event)
  emit('strokeEnd')
}
</script>

<template>
  <div class="matrix-wrap">
    <div
      class="matrix-grid"
      :style="{ gridTemplateColumns }"
      @pointermove="pointerPaint.onPointerMove"
      @pointerup="onContainerPointerUp"
      @pointercancel="onContainerPointerCancel"
    >
      <div class="cell cell--label cell--corner"></div>
      <div v-for="day in days" :key="day.date" class="cell cell--head" :class="{ 'cell--holiday': day.isHoliday }">
        <span class="cell-head__wd">{{ weekdayLabel(day) }}</span>
        <span class="cell-head__dd">{{ dayNumber(day) }}</span>
      </div>
      <div class="cell cell--label cell--foot-head">登</div>
      <div class="cell cell--label cell--foot-head">餘</div>

      <template v-for="(group, groupIndex) in groups" :key="group.groupCode">
        <div class="cell cell--group-head" :style="{ '--group-tint': `var(--group-${Math.min(groupIndex + 1, 4)})` }">
          <span class="group-head__label">{{ group.groupName }}</span>
          <span class="group-head__note">{{ group.capNote }}</span>
        </div>

        <template v-for="row in group.rows" :key="row.staffId">
          <div class="cell cell--label cell--name">
            <span class="row-name__rank">{{ row.rankCode }}</span>
            <span class="row-name__text">{{ row.name }}</span>
          </div>
          <div
            v-for="cellDay in row.cells"
            :key="cellDay.date"
            class="cell cell--day"
            :class="{ 'cell--blocked': cellDay.blocked, 'cell--holiday': cellDay.isHoliday && !cellDay.blocked }"
            :data-paint-key="paintKey(row.staffId, cellDay.date)"
            @pointerdown="onCellPointerDown($event, row.staffId, cellDay.date)"
          ></div>
          <div class="cell cell--count" :class="{ 'cell--over-cap': row.overCap }">{{ row.count }}</div>
          <div class="cell cell--count" :class="{ 'cell--over-cap': row.overCap }">{{ row.remaining }}</div>
        </template>
      </template>

      <div class="cell cell--label cell--foot-label">該日登記人數</div>
      <div v-for="day in days" :key="`count-${day.date}`" class="cell cell--foot">
        {{ footerCounts.get(day.date) ?? 0 }}
      </div>
      <div class="cell cell--foot"></div>
      <div class="cell cell--foot"></div>

      <div class="cell cell--label cell--foot-label">總值可用人數</div>
      <div
        v-for="day in days"
        :key="`chief-${day.date}`"
        class="cell cell--foot"
        :class="{ 'cell--foot-tight': (chiefAvailability.get(day.date) ?? 0) <= 2 }"
      >
        {{ chiefAvailability.get(day.date) ?? 0 }}
      </div>
      <div class="cell cell--foot"></div>
      <div class="cell cell--foot"></div>
    </div>

    <div class="legend">
      <span class="legend__title">圖例</span>
      <span class="legend__item"
        ><span class="legend__swatch legend__swatch--blocked"></span>不可排班日（硬約束，計入 {{ monthlyCap }}
        天上限）</span
      >
      <span class="legend__item"><span class="legend__swatch legend__swatch--holiday"></span>假日（週六、週日與國定假日）</span>
      <span class="legend__note">選好筆刷後點格即可登記／清除，或按住拖過多格連續塗；底部兩列即時顯示該日登記人數與總值可用人數。</span>
    </div>
  </div>
</template>

<style scoped>
.matrix-wrap {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.matrix-grid {
  display: grid;
  grid-auto-rows: 21px;
  overflow: auto;
  border-top: 1px solid var(--color-divider);
  border-left: 1px solid var(--color-divider);
  width: fit-content;
  max-width: 100%;
  touch-action: none;
  user-select: none;
}

.cell {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 10px;
  font-family: var(--font-heading);
  border-right: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
}

.cell--label {
  position: sticky;
  left: 0;
  z-index: 1;
  background: var(--color-bg);
  justify-content: flex-start;
  padding: 0 6px;
}

.cell--corner {
  grid-row: 1;
}

.cell--head {
  flex-direction: column;
  gap: 1px;
  padding: 2px 0;
}

.cell--holiday {
  background: var(--cell-holiday-bg);
}

.cell-head__wd {
  font-size: 8px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.cell-head__dd {
  font-size: 11px;
  font-weight: 600;
}

.cell--foot-head {
  font-size: 10px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  justify-content: center;
}

.cell--group-head {
  grid-column: 1 / -1;
  justify-content: flex-start;
  gap: 8px;
  padding-left: 6px;
  background: color-mix(in srgb, var(--color-text) 4%, transparent);
  position: static;
}

.group-head__label {
  font-size: 11px;
  font-weight: 600;
  padding: 2px 6px;
  background: var(--group-tint, var(--color-accent-200));
  color: var(--color-text);
}

.group-head__note {
  font-size: 10px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.cell--name {
  gap: 6px;
}

.row-name__rank {
  font-size: 9px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  flex: none;
}

.row-name__text {
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cell--day {
  cursor: pointer;
}

.cell--day:hover {
  outline: 1px solid var(--color-accent);
  outline-offset: -1px;
}

.cell--blocked {
  background: var(--cell-blocked-bg);
}

.cell--blocked.cell--holiday {
  background: var(--cell-blocked-bg), var(--cell-holiday-bg);
}

.cell--count {
  font-weight: 600;
  color: color-mix(in srgb, var(--color-text) 60%, transparent);
}

.cell--over-cap {
  background: var(--color-accent);
  color: var(--color-bg);
}

.cell--foot-label {
  font-size: 10px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.cell--foot {
  font-weight: 600;
  font-size: 9.5px;
}

.cell--foot-tight {
  background: var(--color-accent);
  color: var(--color-bg);
}

.legend {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
  font-size: 11px;
}

.legend__title {
  font-family: var(--font-heading);
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.legend__item {
  display: flex;
  align-items: center;
  gap: 5px;
}

.legend__swatch {
  width: 22px;
  height: 15px;
  border: 1px solid color-mix(in srgb, var(--color-text) 10%, transparent);
}

.legend__swatch--blocked {
  background: var(--cell-blocked-bg);
}

.legend__swatch--holiday {
  background: var(--cell-holiday-bg);
}

.legend__note {
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}
</style>
