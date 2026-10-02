<script setup lang="ts">
/**
 * 標題列年月切換器，放在有 `:ym` 的頁面標題左側（issue #52，字級對齊
 * `docs/design-ref/screen-01.html` 的 `ymLabelS` 大字樣式）。資料來自 `GET /schedules`
 * （有值班表的月份與其狀態），是 `useResource` 的示範用法（issue #26）。下拉選單列出
 * 目前年月前後各 12 個月，加上所有有值班表的月份。
 *
 * 標籤格式（issue #62）：大字改成「{YYYY} 年 {M} 月班表」＋ 下拉箭頭，狀態不再塞進標籤
 * 字串，改用標籤右側的狀態 badge（草稿／已發布 vN 用 `ScheduleStatusBadge`，與排班主表標題列同一套顏色；
 * 尚無班表仍是 `.tag.tag-outline`）。原生 `<select>` 關閉時一定會照原樣顯示「選到那個
 * option 的文字」，沒辦法讓觸發器文字跟下拉選單裡的文字不同——這裡疊一層：可見的大字標籤
 * 只是裝飾用 `<span>`（`pointer-events:none`），底下蓋一個文字透明、鋪滿同一個框的 `<select>`
 * 接收點擊與鍵盤操作，選單裡的每個 `<option>` 仍是 `YYYY-MM · 狀態`（原生下拉清單是瀏覽器畫的
 * 獨立圖層，不受上層 `opacity:0` 影響）。
 */
import { computed, ref } from 'vue'
import ScheduleStatusBadge from '@/components/ScheduleStatusBadge.vue'
import { listSchedules } from '@/api/schedules'
import { shiftYearMonth, useYearMonth, yearMonthOptions } from '@/composables/useYearMonth'
import { useResource } from '@/composables/useResource'

const { ym, setYearMonth } = useYearMonth()

const listKey = ref('schedules')
const { data, error } = useResource(listKey, () => listSchedules())

const monthStatus = computed(() => {
  const map = new Map<string, 'draft' | 'published'>()
  for (const month of data.value?.months ?? []) map.set(month.yearMonth, month.status)
  return map
})

// 候選清單與位移邏輯跟 SCREEN 02「指定月份覆寫」的獨立月份選擇器共用，見 useYearMonth.ts。
const options = computed(() => yearMonthOptions(ym.value, [...monthStatus.value.keys()]))

function statusLabelOf(option: string): '草稿' | '已發布' | '尚無班表' {
  const status = monthStatus.value.get(option)
  if (status === 'published') return '已發布'
  if (status === 'draft') return '草稿'
  return '尚無班表'
}

function labelOf(option: string): string {
  return `${option} · ${statusLabelOf(option)}`
}

// 大字標籤：「2026 年 9 月班表」，跟下拉選項的 `YYYY-MM · 狀態` 分開組字串。
const bigLabel = computed(() => {
  const [year, month] = ym.value.split('-')
  return `${year} 年 ${Number(month)} 月班表`
})

// `GET /schedules` 清單載入失敗時 `data` 是 null，這時候不知道 `ym` 真正的狀態，
// 「尚無班表」是 statusLabelOf 查不到資料時的預設值，不能拿它當「確定沒有班表」的結論
// ——寧可不顯示這顆 tag，也不要顯示一個可能是錯的狀態。
const currentStatus = computed(() => (error.value ? null : (monthStatus.value.get(ym.value) ?? 'none')))
const currentVersion = computed(() => data.value?.months.find((m) => m.yearMonth === ym.value)?.publishedVersion)

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
    <div class="ym-switcher__trigger">
      <span class="ym-switcher__label">{{ bigLabel }}<span class="ym-switcher__arrow">▼</span></span>
      <select class="ym-switcher__select" :value="ym" aria-label="選擇年月" @change="onSelect">
        <option v-for="option in options" :key="option" :value="option">{{ labelOf(option) }}</option>
      </select>
    </div>
    <button type="button" class="ym-switcher__step" aria-label="下一個月" @click="shift(1)">›</button>
    <ScheduleStatusBadge
      v-if="currentStatus === 'draft' || currentStatus === 'published'"
      class="ym-switcher__tag"
      :status="currentStatus"
      :published-version="currentVersion"
    />
    <span v-else-if="currentStatus === 'none'" class="tag tag-outline ym-switcher__tag">尚無班表</span>
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

/* 觸發器：可見的大字標籤（`.ym-switcher__label`）決定框的大小，底下疊一個文字透明、
   鋪滿同一個框的 `<select>` 接收點擊——原生下拉選單清單是瀏覽器畫的獨立圖層，
   不受這裡的 `opacity:0` 影響，選單裡的文字（`labelOf` 組的 `YYYY-MM · 狀態`）看得到。 */
.ym-switcher__trigger {
  position: relative;
  display: inline-flex;
  height: 30px;
  border: 1px solid transparent;
}

.ym-switcher__trigger:hover {
  border-color: var(--color-divider);
}

.ym-switcher__label {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 0 var(--space-2);
  font: 600 20px/1.2 var(--font-heading);
  letter-spacing: 0.02em;
  color: var(--color-text);
  white-space: nowrap;
  pointer-events: none;
}

.ym-switcher__arrow {
  font-size: 10px;
  opacity: 0.45;
}

.ym-switcher__select {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  margin: 0;
  padding: 0;
  border: 0;
  background: transparent;
  color: transparent;
  cursor: pointer;
  appearance: none;
}

.ym-switcher__select option {
  background: var(--color-bg);
  color: var(--color-text);
}

.ym-switcher__tag {
  flex: none;
  margin-left: var(--space-2);
}

/* `.tag-outline` 不在 styles.css 的最小共用元件類別裡（只有 tag-neutral／tag-accent），
   跟 UtilizationPanel／DayDetailPanel／ViolationSidebar 一樣補這個 Industry 原版。 */
.tag-outline {
  background: transparent;
  border: 1px solid var(--color-accent);
  color: var(--color-accent);
}
</style>
