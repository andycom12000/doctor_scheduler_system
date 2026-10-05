<script setup lang="ts">
/**
 * 日 × 人：列＝天、欄＝在職人員依 4 身分組分區，右側「空缺」欄（issue #53：印未填補
 * 區域數，0 顯示空白，不再列區域代號清單）。欄很多，欄寬窄、表頭旋轉；第一欄（日期）與
 * 表頭固定（`position: sticky`）——sticky 的容區是外層 `.schedule__main`（見 index.vue），
 * 這裡不設 `overflow`／`max-height`，整月一次放完、不內捲（issue #53）。
 *
 * `staffRenderIndex` 是 index.vue 用 `projectRenderIndexToStaffView` 轉換過的結果——
 * 逐格違規（H1、H2、H5…）原始用 `area:{areaId}:{date}` 記位置，這裡已經轉成
 * 「當天在那個區值班的人」，不用也不該自己再解一次 cellKey。
 *
 * 「當月有班、但已停用」的人不在點數看板裡（點數看板只列在職），額外併一欄「停用」，
 * 不讓他的班憑空從畫面消失；底部「班數」「額度」兩列一併補上他的班數（額度顯示 `—`）。
 *
 * 列印（#32）是案主指定的紙本格式：A4 橫式一頁一個月。印的時候拿掉違規、登記、空缺的
 * 底色（紙本是張貼用的班表，不是工作畫面），只留兩種假日的整列底紋與格內區域代號。
 */
import { computed } from 'vue'
import type { Area, PointBoardGroup } from '@/api/types'
import {
  staffCellKey,
  staffFooterColumns,
  vacancyCountLabel,
  type DayColumn,
  type StaffDirectoryEntry,
} from './lib/scheduleGrid'
import { cellKindOf } from './lib/cellStyle'
import type { CellRenderKind } from './lib/violationStyle'
import { domIdForCellKey } from './lib/cellNav'
import { holidayNote } from './lib/printSheet'

const props = defineProps<{
  areas: Area[]
  days: DayColumn[]
  pointBoardGroups: PointBoardGroup[]
  dutiesByStaff: Map<string, string[]>
  staffRenderIndex: Map<string, CellRenderKind>
  vacancyCounts: Map<string, number>
  staffDirectory: Map<string, StaffDirectoryEntry>
  blockedSet: ReadonlySet<string>
}>()

const emit = defineEmits<{ cellClick: [areaId: string, date: string] }>()

const areaLabelById = computed(() => new Map(props.areas.map((area) => [area.id, area.name])))

/** 當月有班、但不在點數看板（已停用）的人；點數看板只列在職，這裡補一欄不讓班消失。 */
const extraStaffRows = computed<StaffDirectoryEntry[]>(() => {
  const known = new Set<string>()
  for (const group of props.pointBoardGroups) {
    for (const row of group.rows) known.add(row.staffId)
  }
  const seen = new Set<string>()
  const rows: StaffDirectoryEntry[] = []
  for (const key of props.dutiesByStaff.keys()) {
    const staffId = key.slice(0, key.indexOf('|'))
    if (known.has(staffId) || seen.has(staffId)) continue
    seen.add(staffId)
    const entry = props.staffDirectory.get(staffId)
    if (entry) rows.push(entry)
  }
  return rows
})

const totalColumns = computed(
  () => props.pointBoardGroups.reduce((n, g) => n + g.rows.length, 0) + extraStaffRows.value.length,
)

/** 底部「班數」「額度」兩列：依表頭同一個順序（分組欄位 → 停用欄位）攤平。 */
const staffColumns = computed(() =>
  staffFooterColumns(props.pointBoardGroups, extraStaffRows.value, props.dutiesByStaff),
)

/** 整列的假日種類，只有列印用得到（畫面上的假日底色走 `cellKindOf`）。 */
function dayClass(day: DayColumn): Record<string, boolean> {
  return {
    'dp-grid__day--holiday': day.isHoliday && !day.isPublicHoliday,
    'dp-grid__day--public-holiday': day.isPublicHoliday,
  }
}

const publicNote = computed(() => holidayNote(props.days, 'public'))
const otherHolidayNote = computed(() => holidayNote(props.days, 'other'))

/** 當天的區域；同人同日兩區（X1，#68）時有多個。 */
function areasAt(staffId: string, date: string): string[] {
  return props.dutiesByStaff.get(`${staffId}|${date}`) ?? []
}

function cellClass(staffId: string, date: string, isHoliday: boolean): Record<string, boolean> {
  const areaId = areasAt(staffId, date)[0]
  const render = props.staffRenderIndex.get(staffCellKey(staffId, date)) ?? null
  const isBlocked = props.blockedSet.has(`${staffId}|${date}`)
  const kind = cellKindOf({ hasDuty: Boolean(areaId), isHoliday, renderKind: render, isBlocked })
  return {
    // 只有有班的格子點了會開候選人面板（cellClick），才給可點的游標。
    'dp-grid__cell--clickable': Boolean(areaId),
    'dp-grid__cell--duty': kind === 'duty' || kind === 'duty-holiday',
    'dp-grid__cell--duty-holiday': kind === 'duty-holiday',
    'dp-grid__cell--holiday': kind === 'holiday',
    'dp-grid__cell--blocked': kind === 'blocked',
    'dp-grid__cell--violation-bg': kind === 'violation-bg',
    'dp-grid__cell--violation-stripe': kind === 'violation-stripe',
  }
}

function cellText(staffId: string, date: string): string {
  return areasAt(staffId, date)
    .map((id) => areaLabelById.value.get(id) ?? '')
    .join('、')
}

/** 同日排在兩區（X1）的格子才有提示；一般格子不要多餘的 title。 */
function cellTitle(staffId: string, date: string): string | undefined {
  const areas = areasAt(staffId, date)
  if (areas.length < 2) return undefined
  return `同日排在 ${cellText(staffId, date)}，另一區請到區域 × 日修改`
}

function cellClick(staffId: string, date: string): void {
  const areaId = areasAt(staffId, date)[0]
  if (areaId) emit('cellClick', areaId, date)
}
</script>

<template>
  <div class="day-by-staff">
    <div class="dp-grid" :style="{ gridTemplateColumns: `44px repeat(${totalColumns}, var(--dp-col, 23px)) 56px` }">
      <div class="dp-grid__corner"></div>
      <template v-for="group in pointBoardGroups" :key="group.groupCode">
        <div class="dp-grid__group" :style="{ gridColumn: `span ${group.rows.length}` }">
          {{ group.groupCode }} · {{ group.rows.length }} 人
        </div>
      </template>
      <div
        v-if="extraStaffRows.length"
        class="dp-grid__group dp-grid__group--extra"
        :style="{ gridColumn: `span ${extraStaffRows.length}` }"
      >
        停用 · {{ extraStaffRows.length }} 人
      </div>
      <div class="dp-grid__corner dp-grid__corner--vac-head">空缺</div>

      <div class="dp-grid__corner dp-grid__corner--label">日期</div>
      <template v-for="group in pointBoardGroups" :key="`${group.groupCode}-head`">
        <div v-for="row in group.rows" :key="row.staffId" class="dp-grid__head" :title="`${row.name}（${row.rankCode}）`">
          <span class="dp-grid__head-name">{{ row.name }}</span>
          <span class="dp-grid__head-rank">{{ row.rankCode }}</span>
        </div>
      </template>
      <div
        v-for="row in extraStaffRows"
        :key="`extra-${row.staffId}`"
        class="dp-grid__head dp-grid__head--extra"
        :title="`${row.name}（${row.rankCode}）· 已停用`"
      >
        <span class="dp-grid__head-name">{{ row.name }}</span>
        <span class="dp-grid__head-rank">{{ row.rankCode }}停</span>
      </div>
      <div class="dp-grid__corner dp-grid__corner--label dp-grid__corner--vac-sub">未填補<br />區域</div>

      <template v-for="day in days" :key="day.date">
        <div class="dp-grid__label" :class="[{ 'dp-grid__label--holiday': day.isHoliday }, dayClass(day)]">
          {{ day.dd }}<span class="dp-grid__wd">{{ day.weekday }}</span>
        </div>
        <template v-for="group in pointBoardGroups" :key="`${group.groupCode}-${day.date}`">
          <button
            v-for="row in group.rows"
            :id="domIdForCellKey(staffCellKey(row.staffId, day.date)) ?? undefined"
            :key="row.staffId"
            type="button"
            class="dp-grid__cell"
            :class="[cellClass(row.staffId, day.date, day.isHoliday), dayClass(day)]"
            :title="cellTitle(row.staffId, day.date)"
            @click="cellClick(row.staffId, day.date)"
          >
            {{ cellText(row.staffId, day.date) }}
          </button>
        </template>
        <button
          v-for="row in extraStaffRows"
          :id="domIdForCellKey(staffCellKey(row.staffId, day.date)) ?? undefined"
          :key="`extra-${row.staffId}-${day.date}`"
          type="button"
          class="dp-grid__cell"
          :class="[cellClass(row.staffId, day.date, day.isHoliday), dayClass(day)]"
          :title="cellTitle(row.staffId, day.date)"
          @click="cellClick(row.staffId, day.date)"
        >
          {{ cellText(row.staffId, day.date) }}
        </button>
        <div
          class="dp-grid__vac"
          :class="[{ 'dp-grid__vac--some': (vacancyCounts.get(day.date) ?? 0) > 0 }, dayClass(day)]"
        >
          {{ vacancyCountLabel(vacancyCounts.get(day.date) ?? 0) }}
        </div>
      </template>

      <div class="dp-grid__foot-label">班數</div>
      <template v-for="col in staffColumns" :key="`duties-${col.staffId}`">
        <div class="dp-grid__foot">{{ col.duties }}</div>
      </template>
      <div class="dp-grid__foot"></div>

      <div class="dp-grid__foot-label">額度</div>
      <template v-for="col in staffColumns" :key="`quota-${col.staffId}`">
        <div class="dp-grid__foot" :class="{ 'dp-grid__foot--at-cap': col.quotaAtCap }" :title="col.quotaTitle">
          {{ col.quotaLabel }}
        </div>
      </template>
      <div class="dp-grid__foot"></div>
    </div>

    <div class="ad-legend screen-only">
      <span class="k">圖例</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--duty" />平日值班（格內為區域代號）</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--duty-holiday" />假日值班</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--blocked" />不可排班日登記</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--holiday" />假日（整列底色）</span>
      <span class="ad-legend__item"><span class="ad-legend__swatch ad-legend__swatch--vacancy" />空缺欄有數字＝該日有未填補區域（H1）</span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch ad-legend__swatch--violation-bg" />其他硬違規（H2／H3／H4／H6／H7）
      </span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch ad-legend__swatch--violation" />排到已登記的不可排班日（H5）
      </span>
      <span class="dp-note">格內為區域代號 · 空白＝未值班 · 「停用」欄是當月仍有班、後來被停用的人</span>
    </div>

    <div class="ad-legend print-legend print-only">
      <span class="ad-legend__item"><span class="ad-legend__swatch print-legend__holiday" />假日（國定假日除外）{{ otherHolidayNote ? `：${otherHolidayNote}` : '' }}</span>
      <span class="ad-legend__item">
        <span class="ad-legend__swatch print-legend__public-holiday" />國定假日{{ publicNote ? `：${publicNote}` : '' }}
      </span>
      <span class="dp-note">格內為區域代號 · 空白＝未值班 · 空缺欄＝當日未填補區域數</span>
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

.day-by-staff {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-width: 0;
}

.dp-grid {
  /* 身分組群組列的固定高度：群組列自己、表頭列的 sticky `top`、日期角落格的 `top` 都吃這一個值，
     改高度時不會讓表頭蓋住或漏出群組列（原本散落三處的 20px 魔術數）。 */
  --dp-group-row-h: 20px;
  display: grid;
  align-content: start;
  /* 不設 overflow／max-height：整月一次放完，不再內捲（issue #53）。sticky 表頭改吃外層
     `.schedule__main`（見 index.vue）的 `overflow: auto` 當捲動容器，同一份 top 偏移量繼續有效。 */
  border-top: 1px solid var(--color-divider);
  border-left: 1px solid var(--color-divider);
}

.dp-grid__corner,
.dp-grid__group,
.dp-grid__head,
.dp-grid__label,
.dp-grid__cell,
.dp-grid__vac,
.dp-grid__foot-label,
.dp-grid__foot {
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
  top: var(--dp-group-row-h);
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
  box-sizing: border-box;
  height: var(--dp-group-row-h);
  display: flex;
  align-items: center;
  justify-content: center;
}

.dp-grid__group--extra {
  background: color-mix(in srgb, var(--color-text) 16%, transparent);
  color: color-mix(in srgb, var(--color-text) 70%, transparent);
}

.dp-grid__head {
  position: sticky;
  top: var(--dp-group-row-h);
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

.dp-grid__head--extra {
  opacity: 0.75;
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
  /* 空格點了沒有反應（日 × 人只有有班的格子能開候選人面板），不給可點的游標。 */
  cursor: default;
  font: 600 9px ui-monospace, Menlo, monospace;
  color: var(--color-accent-900);
  /* 同日兩區（X1）顯示「A、B」可能比格子寬，截斷不撐破（完整內容在 title） */
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.dp-grid__cell--clickable {
  cursor: pointer;
}

.dp-grid__cell:hover {
  outline: 1px solid var(--color-accent);
  outline-offset: -1px;
}

.dp-grid__cell--duty {
  background: var(--cell-duty-bg);
}

.dp-grid__cell--duty-holiday {
  background: var(--cell-duty-holiday-bg);
}

.dp-grid__cell--holiday {
  background: var(--cell-holiday-bg);
}

.dp-grid__cell--blocked {
  background: var(--cell-blocked-bg);
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

/* 「空缺」表頭是最後一欄，不是第一欄——`.dp-grid__corner` 的 `left: 0` 是給左上角那格
   （日期欄）用的，這兩格繼承到同一個 `left: 0` 會讓它們橫向捲動時飄去黏在左邊、蓋住
   當時剛好捲到左緣的身分組表頭（審查回饋 V1：曾跟「NP · 1 人」疊在一起）。只留 top 的
   sticky，水平方向照 grid 正常排版跟著捲。 */
.dp-grid__corner--vac-head,
.dp-grid__corner--vac-sub {
  left: auto;
}

.dp-grid__corner--vac-head {
  font: 600 10px var(--font-heading);
}

.dp-grid__corner--vac-sub {
  line-height: 1.3;
}

.dp-grid__foot-label {
  position: sticky;
  left: 0;
  z-index: 1;
  background: var(--color-bg);
  display: flex;
  align-items: center;
  padding-left: 4px;
  font: 600 10px var(--font-heading);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  border-top: 1px solid var(--color-divider);
}

.dp-grid__foot {
  display: flex;
  align-items: center;
  justify-content: center;
  font: 600 9.5px ui-monospace, Menlo, monospace;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  border-top: 1px solid var(--color-divider);
}

/* 額度已排打平上限：上色提醒（設計稿 dpFoot 行為，審查回饋 N7）。 */
.dp-grid__foot--at-cap {
  color: var(--color-accent-800);
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

/*
  列印（#32）：A4 橫式可印範圍約 281 × 194 mm。31 天 × 34 人要一頁放完，列高與表頭都要比
  畫面矮；欄寬改成平分整頁寬（`--dp-col`，表頭欄數是 inline style，只能透過變數改）。
  sticky 在紙上沒有意義，全部改回一般排版。
*/
@media print {
  .day-by-staff {
    gap: 4px;
  }

  .dp-grid {
    --dp-col: minmax(0, 1fr);
    border-color: var(--print-rule);
  }

  .dp-grid__corner,
  .dp-grid__group,
  .dp-grid__head,
  .dp-grid__label,
  .dp-grid__cell,
  .dp-grid__vac,
  .dp-grid__foot-label,
  .dp-grid__foot {
    position: static;
    border-color: var(--print-rule);
    color: #000;
  }

  .dp-grid__group {
    height: auto;
    padding: 1px 0;
    background: #fff;
    font-size: 9px;
  }

  .dp-grid__head {
    height: 58px;
    padding: 2px 0;
    gap: 2px;
  }

  .dp-grid__head-name {
    font-size: 9.5px;
    letter-spacing: 0;
  }

  .dp-grid__head-rank {
    font-size: 8px;
    color: #000;
  }

  .dp-grid__label,
  .dp-grid__corner {
    background: #fff;
  }

  .dp-grid__wd {
    color: #000;
  }

  /* 違規、不可排班日登記、空缺的底色與外框一律拿掉，只留整列假日底紋（見下） */
  .dp-grid__cell,
  .dp-grid__vac {
    height: 17px;
    background: #fff;
    box-shadow: none;
    font-size: 9.5px;
  }

  .dp-grid__vac--some {
    font-weight: 700;
  }

  .dp-grid__day--holiday {
    background: var(--print-holiday-bg);
  }

  .dp-grid__day--public-holiday {
    background: var(--print-public-holiday-bg);
  }

  .dp-grid__foot-label,
  .dp-grid__foot {
    padding: 1px 0;
    font-size: 9px;
  }

  .print-legend {
    display: flex;
    gap: var(--space-4);
    font-size: 10px;
  }

  .print-legend .dp-note {
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
