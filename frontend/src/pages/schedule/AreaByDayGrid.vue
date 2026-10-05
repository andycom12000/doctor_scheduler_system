<script setup lang="ts">
/**
 * 區域 × 日：列＝5 區（依區域類型分組顯示，比照設計稿的一般病房／ICU／總值三段），
 * 欄＝天。值班格底色照該員身分組（`--group-1..4`，PR #44 review：身分組圖例四色塊
 * 不能全同色）；假日值班在值班格上疊一層外框，不再用另一種底色蓋掉組別顏色。
 * 空缺／違規疊上 `lib/cellStyle.ts` 算出的優先序，一律蓋過值班／假日的底色。
 *
 * 拖拉對調（issue #34）：值班格按住拖到另一格（空格也行，等於搬移）放開即 emit `swap`；
 * 來源格與目標格有預覽樣式、Esc 取消、位移很小仍是點擊（開候選人面板）。
 * 格子鍵就是 CellRef（`areaId|date`），本元件不呼叫 API，由 index.vue 接 `swap`。
 *
 * 列印（#32）：驗收以日 × 人為準，這裡給一份同規格的紙本（A4 橫式一頁）。印的時候拿掉違規與
 * 空缺的底色（空缺格仍印「缺」），值班格留身分組色階，兩種假日畫在日期表頭與空格上。
 */
import { computed } from 'vue'
import type { Area, AreaType, PointBoardGroup } from '@/api/types'
import {
  abbreviate,
  areaCellKey,
  filledCountByDate,
  groupAreasByType,
  shortStaffCode,
  type DayColumn,
  type StaffDirectoryEntry,
} from './lib/scheduleGrid'
import { cellKindOf, groupColorIndex } from './lib/cellStyle'
import type { CellRenderKind } from './lib/violationStyle'
import { domIdForCellKey } from './lib/cellNav'
import { parseSwapCellKey, swapCellKey } from './lib/writeFlow'
import { holidayNote } from './lib/printSheet'
import { usePointerDragSwap } from '@/composables/usePointerDragSwap'

const props = defineProps<{
  areaTypes: AreaType[]
  areas: Area[]
  days: DayColumn[]
  dutyMap: Map<string, string>
  staffDirectory: Map<string, StaffDirectoryEntry>
  cellRenderIndex: Map<string, CellRenderKind>
  vacancyCounts: Map<string, number>
  pointBoardGroups: PointBoardGroup[]
  /** 寫入進行中：不接受新的拖拉起手（避免同時兩筆對調）。 */
  swapDisabled?: boolean
}>()

const emit = defineEmits<{
  cellClick: [areaId: string, date: string]
  swap: [a: { areaId: string; date: string }, b: { areaId: string; date: string }]
}>()

const dragSwap = usePointerDragSwap({
  // 只有有人的格子能當來源；空格拖不起來，點它仍是開候選人面板。
  canStart: (key) => !props.swapDisabled && props.dutyMap.has(key),
  onDrop: (sourceKey, targetKey) => {
    const a = parseSwapCellKey(sourceKey)
    const b = parseSwapCellKey(targetKey)
    if (a && b) emit('swap', a, b)
  },
})

function onCellClick(areaId: string, date: string): void {
  // 剛拖過（放下或 Esc 取消）之後瀏覽器仍會補一個 click，吞掉才不會開出候選人面板。
  if (dragSwap.consumeClickSuppression()) return
  emit('cellClick', areaId, date)
}

// `/settings/areas` 失敗時 areaTypes 是空的：退回不分組列出 `schedule.areas`，不讓格線整片空白。
const groups = computed(() => groupAreasByType(props.areaTypes, props.areas))

/** 身分組圖例：色階依組在點數看板出現的順序分配，標籤用真實 `groupName`，不寫死。 */
const groupLegend = computed(() =>
  props.pointBoardGroups.map((group, index) => ({ colorIndex: groupColorIndex(index), name: group.groupName })),
)

function staffIdAt(areaId: string, date: string): string | undefined {
  return props.dutyMap.get(`${areaId}|${date}`)
}

function directoryOf(staffId: string | undefined): StaffDirectoryEntry | undefined {
  return staffId ? props.staffDirectory.get(staffId) : undefined
}

function cellClass(areaId: string, date: string, isHoliday: boolean): Record<string, boolean> {
  const staffId = staffIdAt(areaId, date)
  const render = props.cellRenderIndex.get(areaCellKey(areaId, date)) ?? null
  const kind = cellKindOf({ hasDuty: Boolean(staffId), isHoliday, renderKind: render })
  const classes: Record<string, boolean> = {
    'ad-grid__cell--duty': kind === 'duty' || kind === 'duty-holiday',
    'ad-grid__cell--duty-holiday': kind === 'duty-holiday',
    'ad-grid__cell--holiday': kind === 'holiday',
    'ad-grid__cell--vacancy': kind === 'vacancy',
    'ad-grid__cell--violation-stripe': kind === 'violation-stripe',
    'ad-grid__cell--violation-bg': kind === 'violation-bg',
  }
  const groupIndex = directoryOf(staffId)?.groupIndex
  if (staffId && groupIndex !== null && groupIndex !== undefined) {
    const color = groupColorIndex(groupIndex)
    if (kind === 'duty' || kind === 'duty-holiday') classes[`ad-grid__cell--group-${color}`] = true
    // 列印時違規／空缺底色拿掉，有人的格子都要回到組別色；只在 @media print 有樣式，畫面不受影響。
    classes[`ad-grid__cell--print-group-${color}`] = true
  }
  const drag = dragSwap.state.value
  const key = swapCellKey(areaId, date)
  if (drag?.sourceKey === key) classes['ad-grid__cell--drag-source'] = true
  if (drag?.overKey === key) classes['ad-grid__cell--drag-over'] = true
  return classes
}

/** 空格一律印「缺」（比照設計稿），值班格印姓名簡稱；兩邊名冊都查不到時退回 staffId 短碼。 */
function cellText(areaId: string, date: string): string {
  const staffId = staffIdAt(areaId, date)
  if (!staffId) return '缺'
  const dir = directoryOf(staffId)
  return dir ? abbreviate(dir.name) : shortStaffCode(staffId)
}

/** 日期欄的假日種類，只有列印用得到（畫面上的假日底色走 `cellKindOf`）。 */
function dayClass(day: DayColumn): Record<string, boolean> {
  return {
    'ad-grid__day--holiday': day.isHoliday && !day.isPublicHoliday,
    'ad-grid__day--public-holiday': day.isPublicHoliday,
  }
}

const publicNote = computed(() => holidayNote(props.days, 'public'))
const otherHolidayNote = computed(() => holidayNote(props.days, 'other'))

function filled(areaId: string): number {
  return props.days.filter((day) => staffIdAt(areaId, day.date)).length
}
</script>

<template>
  <div class="area-by-day">
    <div
      class="ad-grid"
      :class="{ 'ad-grid--dragging': dragSwap.state.value !== null }"
      :style="{ gridTemplateColumns: `132px repeat(${days.length}, var(--ad-col, 26px)) 44px` }"
      @pointermove="dragSwap.onPointerMove"
      @pointerup="dragSwap.onPointerUp"
      @pointercancel="dragSwap.onPointerCancel"
    >
      <div class="ad-grid__corner">區域 / 日期</div>
      <div v-for="day in days" :key="day.date" class="ad-grid__head"
        :class="[{ 'ad-grid__head--holiday': day.isHoliday }, dayClass(day)]"
      >
        <span class="ad-grid__wd">{{ day.weekday }}</span>
        <span class="ad-grid__dd">{{ day.dd }}</span>
        <span class="ad-grid__pt">{{ day.quotaPointValue }}</span>
      </div>
      <div class="ad-grid__corner">填補</div>

      <template v-for="group in groups" :key="group.areaType?.code ?? '__ungrouped'">
        <div v-if="group.areaType" class="ad-grid__group" :style="{ gridColumn: `1 / span ${days.length + 2}` }">
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
            :class="[cellClass(area.id, day.date, day.isHoliday), dayClass(day)]"
            :data-swap-key="swapCellKey(area.id, day.date)"
            @pointerdown="dragSwap.onCellPointerDown($event, swapCellKey(area.id, day.date))"
            @click="onCellClick(area.id, day.date)"
          >
            {{ cellText(area.id, day.date) }}
          </button>
          <div class="ad-grid__stat">{{ filled(area.id) }}/{{ days.length }}</div>
        </template>
      </template>

      <div class="ad-grid__label ad-grid__label--foot">每日已填補</div>
      <div v-for="day in days" :key="day.date" class="ad-grid__fill">
        {{ filledCountByDate(areas.length, vacancyCounts, day.date) }}
      </div>
      <div class="ad-grid__fill"></div>
    </div>

    <div class="ad-legend screen-only">
      <span class="k">身分組</span>
      <span v-for="g in groupLegend" :key="g.colorIndex" class="ad-legend__item">
        <span class="ad-legend__swatch" :class="`ad-legend__swatch--group-${g.colorIndex}`" />{{ g.name }}
      </span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--holiday" />假日（整列底色）</span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch ad-legend__swatch--vacancy" />空缺（H1 · 登記過多時的正常結果）
      </span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch ad-legend__swatch--violation-bg" />其他硬違規（H2／H3／H4／H6／H7）
      </span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch ad-legend__swatch--violation" />排到已登記的不可排班日（H5）
      </span>
      <span class="dp-note">格內為姓名簡稱 · 假日值班疊外框</span>
    </div>

    <div class="ad-legend print-legend print-only">
      <span class="k">身分組</span>
      <span v-for="g in groupLegend" :key="g.colorIndex" class="ad-legend__item">
        <span class="ad-legend__swatch" :class="`ad-legend__swatch--group-${g.colorIndex}`" />{{ g.name }}
      </span>
      <span class="ad-legend__item"><span class="ad-legend__swatch print-legend__holiday" />假日（國定假日除外）{{ otherHolidayNote ? `：${otherHolidayNote}` : '' }}</span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch print-legend__public-holiday" />國定假日{{ publicNote ? `：${publicNote}` : '' }}
      </span>
      <span class="dp-note">格內為姓名簡稱 · 缺＝空缺</span>
    </div>
  </div>
</template>

<style scoped>
.k {
  font: 600 10px/1 var(--font-heading);
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

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
  max-height: 480px;
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

.ad-grid__cell {
  user-select: none;
}

/* 拖拉預覽：來源格半透明、目標格虛線外框；拖曳中整個矩陣游標改成 grabbing。 */
.ad-grid--dragging,
.ad-grid--dragging .ad-grid__cell {
  cursor: grabbing;
}

.ad-grid__cell--drag-source {
  opacity: 0.45;
}

.ad-grid__cell--drag-over {
  outline: 2px dashed var(--color-accent);
  outline-offset: -2px;
}

.ad-grid__cell:hover {
  outline: 1px solid var(--color-accent);
  outline-offset: -1px;
}

.ad-grid__cell--duty {
  background: var(--cell-duty-bg);
}

/* 4 身分組色階：只在「值班、沒有更高優先序的狀態」時套用，蓋掉上面的預設值班底色。 */
.ad-grid__cell--group-1 {
  background: var(--group-1);
  color: var(--group-1-fg);
}

.ad-grid__cell--group-2 {
  background: var(--group-2);
  color: var(--group-2-fg);
}

.ad-grid__cell--group-3 {
  background: var(--group-3);
  color: var(--group-3-fg);
}

.ad-grid__cell--group-4 {
  background: var(--group-4);
  color: var(--group-4-fg);
}

/* 假日值班：不换底色（身分組顏色才是重點），疊一層外框標記假日。 */
.ad-grid__cell--duty-holiday {
  box-shadow: inset 0 0 0 1.5px var(--cell-duty-holiday-bg);
}

/* 假日空格（沒有值班、也沒有違規）：整格套假日底色，比照設計稿「假日（整列底色）」。 */
.ad-grid__cell--holiday {
  background: var(--cell-holiday-bg);
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

.ad-legend__swatch--group-1 {
  background: var(--group-1);
}

.ad-legend__swatch--group-2 {
  background: var(--group-2);
}

.ad-legend__swatch--group-3 {
  background: var(--group-3);
}

.ad-legend__swatch--group-4 {
  background: var(--group-4);
}

.ad-legend__swatch--holiday {
  background: var(--cell-holiday-bg);
}

.ad-legend__swatch--vacancy {
  background: var(--cell-vacancy-bg);
}

.ad-legend__swatch--violation-bg {
  background: var(--cell-violation-bg);
}

.ad-legend__swatch--violation {
  background: var(--cell-violation-stripe);
}

.dp-note {
  font-size: 11px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

/* 列印（#32）：解掉矩陣自己的捲動框與 sticky，欄寬平分整頁寬（`--ad-col`，表頭欄數是 inline style）。 */
@media print {
  .ad-grid {
    --ad-col: minmax(0, 1fr);
    overflow: visible;
    max-height: none;
    border-color: var(--print-rule);
  }

  .ad-grid__corner,
  .ad-grid__head,
  .ad-grid__group,
  .ad-grid__label,
  .ad-grid__cell,
  .ad-grid__stat,
  .ad-grid__fill {
    position: static;
    border-color: var(--print-rule);
    color: #000;
  }

  .ad-grid__corner,
  .ad-grid__head,
  .ad-grid__group,
  .ad-grid__label {
    background: #fff;
  }

  .ad-grid__wd,
  .ad-grid__pt {
    color: #000;
  }

  /* 違規與空缺的底色、外框一律拿掉，再依序疊回假日底紋與身分組色階 */
  .ad-grid__cell {
    background: #fff;
    box-shadow: none;
    outline: none;
  }

  .ad-grid__cell--vacancy {
    font-weight: 700;
  }

  .ad-grid__day--holiday {
    background: var(--print-holiday-bg);
  }

  .ad-grid__day--public-holiday {
    background: var(--print-public-holiday-bg);
  }

  .ad-grid__cell--print-group-1 {
    background: var(--group-1);
    color: var(--group-1-fg);
  }

  .ad-grid__cell--print-group-2 {
    background: var(--group-2);
    color: var(--group-2-fg);
  }

  .ad-grid__cell--print-group-3 {
    background: var(--group-3);
    color: var(--group-3-fg);
  }

  .ad-grid__cell--print-group-4 {
    background: var(--group-4);
    color: var(--group-4-fg);
  }

  .print-legend {
    display: flex;
    font-size: 10px;
  }

  .print-legend .dp-note,
  .print-legend .k {
    font-size: 10px;
    color: #000;
  }

  .print-legend__holiday {
    background: var(--print-holiday-bg);
  }

  .print-legend__public-holiday {
    background: var(--print-public-holiday-bg);
  }
}
</style>
