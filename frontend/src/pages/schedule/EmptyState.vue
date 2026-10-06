<script setup lang="ts">
/**
 * SCREEN 01 V02 月份空狀態（issue #62）：`GET /schedules/{ym}` 404 時，把該月「日 × 人」
 * 矩陣骨架先畫出來（人員依身分組排列 × 當月日數），訊息面板霧化疊在骨架中央。
 * 求解完成後同一組格線會被填滿，版面不跳動（規格 §1）。
 *
 * 格子不可點（規格 §6）：矩陣純骨架，不接受 hover／指派，這裡全部是 `<div>` 不是 `<button>`。
 * 面板內只有一句話和一個動作——不放人數／日數這類統計，也不加次要按鈕（規格 §5.2）。
 */
import { computed, type CSSProperties } from 'vue'
import type { Rank, RankGroup, Staff } from '@/api/types'
import type { DayColumn } from './lib/scheduleGrid'
import {
  emptyStateTitle,
  groupBandsOf,
  groupBoundaryColumns,
  orderStaffByGroup,
  panelRectStyle,
} from './lib/emptyGrid'

const props = defineProps<{
  ym: string
  staff: Staff[]
  ranks: Rank[]
  groups: RankGroup[]
  days: DayColumn[]
  solving: boolean
  solveError: string | null
}>()

const emit = defineEmits<{ solve: [] }>()

const columns = computed(() => orderStaffByGroup(props.staff, props.ranks, props.groups))
const bands = computed(() => groupBandsOf(columns.value))
const boundarySet = computed(() => groupBoundaryColumns(columns.value))
const panelStyle = computed(() => panelRectStyle(columns.value.length, props.days.length) as CSSProperties)
const titleText = computed(() => emptyStateTitle(props.ym))
</script>

<template>
  <div class="empty-state">
    <div class="empty-state__stage">
      <div class="empty-matrix">
        <div class="empty-matrix__row empty-matrix__row--bands">
          <div class="empty-matrix__corner empty-matrix__corner--band"></div>
          <div
            v-for="band in bands"
            :key="band.groupIndex"
            class="empty-matrix__band"
            :class="`empty-matrix__band--${band.groupIndex}`"
            :style="{ flex: band.count }"
          >
            {{ band.label }} · {{ band.count }} 人
          </div>
        </div>

        <div class="empty-matrix__row empty-matrix__row--names">
          <div class="empty-matrix__corner empty-matrix__corner--name"></div>
          <div
            v-for="(col, i) in columns"
            :key="col.staffId"
            class="empty-matrix__name"
            :class="{ 'empty-matrix__col--boundary': boundarySet.has(i) }"
          >
            <span class="empty-matrix__name-text">{{ col.name }}</span>
          </div>
        </div>

        <div class="empty-matrix__row empty-matrix__row--ranks">
          <div class="empty-matrix__corner empty-matrix__corner--rank k">身分</div>
          <div
            v-for="(col, i) in columns"
            :key="col.staffId"
            class="empty-matrix__rank"
            :class="{ 'empty-matrix__col--boundary': boundarySet.has(i) }"
          >
            {{ col.rankCode }}
          </div>
        </div>

        <div
          v-for="day in days"
          :key="day.date"
          class="empty-matrix__row empty-matrix__row--day"
          :class="{ 'empty-matrix__row--holiday': day.isHoliday }"
        >
          <div class="empty-matrix__date">
            {{ day.dd }}<span class="empty-matrix__weekday">{{ day.weekday }}</span>
          </div>
          <div
            v-for="(col, i) in columns"
            :key="col.staffId"
            class="empty-matrix__cell"
            :class="{ 'empty-matrix__col--boundary': boundarySet.has(i) }"
          ></div>
        </div>

        <div class="empty-matrix__mask" aria-hidden="true"></div>

        <div class="empty-matrix__panel blueprint" :style="panelStyle">
          <i class="corner tl" aria-hidden="true"></i>
          <i class="corner tr" aria-hidden="true"></i>
          <i class="corner bl" aria-hidden="true"></i>
          <i class="corner br" aria-hidden="true"></i>
          <span class="k">SCHEDULE NOT GENERATED</span>
          <div class="empty-matrix__title">{{ titleText }}</div>
          <button
            type="button"
            class="btn btn-primary empty-matrix__cta blueprint"
            :disabled="solving"
            @click="emit('solve')"
          >
            <i class="corner tl" aria-hidden="true"></i>
            <i class="corner tr" aria-hidden="true"></i>
            <i class="corner bl" aria-hidden="true"></i>
            <i class="corner br" aria-hidden="true"></i>
            {{ solving ? '求解中…' : '開始求解' }}
          </button>
          <p v-if="solveError" class="empty-matrix__error">{{ solveError }}</p>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.k {
  font: 600 10px/1 var(--font-heading);
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 70%, transparent);
}

/* PageLayout 傳了 `fill`：body 本身 padding 已歸零、`display:flex`，這裡自己撐滿 */
.empty-state {
  flex: 1;
  min-width: 0;
  min-height: 0;
  display: flex;
}

/* 規格 §3：矩陣區 flex:1，四邊 padding 16px（不用 --space-4，那是 13.6px，跟規格釘的 16px 對不上）。 */
.empty-state__stage {
  flex: 1;
  min-width: 0;
  min-height: 0;
  padding: 16px;
  display: flex;
}

.empty-matrix {
  position: relative;
  flex: 1;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.empty-matrix__row {
  display: flex;
}

/* 表頭三列固定高（規格 §4：18 + 76 + 24 = 118，跟 emptyGrid.ts 的 HEADER_HEIGHT_PX 對齊）。 */
.empty-matrix__row--bands {
  flex: none;
  height: 18px;
}

.empty-matrix__row--names {
  flex: none;
  height: 76px;
}

.empty-matrix__row--ranks {
  flex: none;
  height: 24px;
}

/* 日期列：flex:1 平分剩餘高度，不用 floor 算列高（規格 §4）。 */
.empty-matrix__row--day {
  flex: 1;
  min-height: 0;
}

.empty-matrix__corner {
  width: 44px;
  flex: none;
}

.empty-matrix__corner--band {
  height: 18px;
}

.empty-matrix__corner--name {
  height: 76px;
  border-right: 1px solid color-mix(in srgb, var(--color-text) 14%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
}

.empty-matrix__corner--rank {
  height: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-right: 1px solid color-mix(in srgb, var(--color-text) 14%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 14%, transparent);
}

/* 身分組色帶：底色 accent 12% / 30% / 55% / dim(12)，字色分別 accent-800 / -900 / bg / dim(70)
   （規格 §4 表頭三列）。人數決定 flex 寬度，這裡只給色帶本身的視覺樣式。 */
.empty-matrix__band {
  min-width: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font: 600 10px/1 var(--font-heading);
  letter-spacing: 0.1em;
  border-right: 1px solid var(--color-bg);
}

.empty-matrix__band--0 {
  background: color-mix(in srgb, var(--color-accent) 12%, transparent);
  color: var(--color-accent-800);
}

.empty-matrix__band--1 {
  background: color-mix(in srgb, var(--color-accent) 30%, transparent);
  color: var(--color-accent-900);
}

.empty-matrix__band--2 {
  background: color-mix(in srgb, var(--color-accent) 55%, transparent);
  color: var(--color-bg);
}

.empty-matrix__band--3 {
  background: color-mix(in srgb, var(--color-text) 12%, transparent);
  color: color-mix(in srgb, var(--color-text) 70%, transparent);
}

.empty-matrix__name {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-right: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
  overflow: hidden;
}

.empty-matrix__name-text {
  writing-mode: vertical-rl;
  text-orientation: upright;
  font: 500 11px/1 'Noto Sans TC', 'Microsoft JhengHei UI', sans-serif;
  letter-spacing: 0.06em;
  white-space: nowrap;
}

.empty-matrix__rank {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font: 600 9px/1 var(--font-heading);
  letter-spacing: 0.04em;
  color: color-mix(in srgb, var(--color-text) 58%, transparent);
  border-right: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 14%, transparent);
}

/* 身分組交界欄加重左框（規格 §4：dim(22)），由 boundarySet 動態決定落在哪幾欄。 */
.empty-matrix__col--boundary {
  border-left: 1px solid color-mix(in srgb, var(--color-text) 22%, transparent);
}

.empty-matrix__date {
  width: 44px;
  flex: none;
  display: flex;
  align-items: center;
  gap: 4px;
  padding-left: 5px;
  font: 600 11px/1 var(--font-heading);
  border-right: 1px solid color-mix(in srgb, var(--color-text) 14%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
}

.empty-matrix__weekday {
  font: 400 8.5px/1 'Noto Sans TC', 'Microsoft JhengHei UI', sans-serif;
  color: color-mix(in srgb, var(--color-text) 45%, transparent);
}

.empty-matrix__cell {
  flex: 1;
  min-width: 0;
  border-right: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
}

/* 假日整列底色 dim(9)，星期字改 accent-700；平日星期字 dim(45)（規格 §4）。 */
.empty-matrix__row--holiday .empty-matrix__date,
.empty-matrix__row--holiday .empty-matrix__cell {
  background: color-mix(in srgb, var(--color-text) 9%, transparent);
}

.empty-matrix__row--holiday .empty-matrix__weekday {
  color: var(--color-accent-700);
}

/* 霧化遮罩：blur 2.5px + 58% 紙色，不支援 backdrop-filter 時退回 72% 純色（規格 §5.1）。 */
.empty-matrix__mask {
  position: absolute;
  inset: 0;
  pointer-events: none;
  backdrop-filter: blur(2.5px);
  background: color-mix(in srgb, var(--color-bg) 58%, transparent);
}

@supports not (backdrop-filter: blur(1px)) {
  .empty-matrix__mask {
    background: color-mix(in srgb, var(--color-bg) 72%, transparent);
  }
}

/* `.blueprint` 四角記號不在 styles.css 的最小共用元件類別裡（那份只有 `.btn`／`.tag`），
   這裡照 docs/design-ref/industry.css 的 `.corner` 補一份，兩處用（面板本體＋按鈕）。
   這個區塊要排在 `.empty-matrix__panel` 之前——兩者都是單一 class 選擇器、優先度相同，
   CSS 疊層規則是後面贏，面板需要的 `position:absolute` 得寫在 `.blueprint` 的
   `position:relative` 之後才不會被蓋掉（PR 自測抓到：面板疊在 `.blueprint` 後面時整個
   掉到矩陣最下方、被 `overflow:hidden` 裁光，畫面上完全看不到）。 */
.blueprint {
  position: relative;
}

.empty-matrix__panel {
  position: absolute;
  background: var(--color-bg);
  border: 1px solid var(--color-accent);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 11px;
  text-align: center;
  padding: 0 16px;
  /* 規格 §7：面板最小寬度以標題不換行為準——`panelRectStyle` 的寬度是格數算出來的比例，
     視窗窄（例如 1024px）時可能小於標題本身的排版寬度，這裡讓內容（標題）撐開面板，
     不讓標題換行撐破框。 */
  min-width: max-content;
}

.blueprint > .corner {
  position: absolute;
  width: 11px;
  height: 11px;
  color: var(--color-accent);
}

.blueprint > .corner::before,
.blueprint > .corner::after {
  content: '';
  position: absolute;
  background: currentColor;
}

.blueprint > .corner::before {
  left: 5px;
  top: 0;
  width: 1px;
  height: 100%;
}

.blueprint > .corner::after {
  top: 5px;
  left: 0;
  width: 100%;
  height: 1px;
}

.blueprint > .corner.tl {
  top: -6px;
  left: -6px;
}

.blueprint > .corner.tr {
  top: -6px;
  right: -6px;
}

.blueprint > .corner.bl {
  bottom: -6px;
  left: -6px;
}

.blueprint > .corner.br {
  bottom: -6px;
  right: -6px;
}

.empty-matrix__title {
  font: 600 34px/1 var(--font-heading);
  white-space: nowrap;
}

.empty-matrix__cta {
  margin-top: 4px;
  padding: 10px 22px;
  font: 600 13.5px/1 var(--font-heading);
  letter-spacing: 0.05em;
}

.empty-matrix__error {
  margin: 0;
  font-size: 11.5px;
  color: var(--color-accent-900);
}
</style>
