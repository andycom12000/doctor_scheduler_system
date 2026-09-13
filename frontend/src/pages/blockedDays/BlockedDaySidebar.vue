<script setup lang="ts">
/**
 * 側欄：登記概況（總筆數／超過上限／尚未登記）＋可行性預警兩層
 * （逐日 shortages、整月 bySupply 三層供需，`bySupply` 恰好三層時附「R2/R3 優先 ICU
 * 會被犧牲」提示，判斷邏輯全在 `logic.ts` 的純函式，這裡只負責顯示）。
 */
import { computed } from 'vue'
import type { FeasibilityReport } from '@/api/types'
import { computeSupplyRatio, type ShortageDayView, type StaffCountView, type WardSqueezeHint } from './logic'

const props = defineProps<{
  totalCount: number
  activeStaffCount: number
  monthlyCap: number
  overList: StaffCountView[]
  noneList: StaffCountView[]
  shortageDays: ShortageDayView[]
  bySupply: FeasibilityReport['bySupply']
  areaTypeNameByCode: Record<string, string>
  wardSqueezeHint: WardSqueezeHint | null
}>()

/** 每層供需疊上 `computeSupplyRatio` 的倍率／進度條/％，模板不重複呼叫純函式。 */
const supplyLayers = computed(() => props.bySupply.map((layer) => ({ layer, ratio: computeSupplyRatio(layer) })))

function layerLabel(codes: string[], areaTypeNameByCode: Record<string, string>): string {
  return codes.map((code) => areaTypeNameByCode[code] ?? code).join('＋')
}

function joinNames(list: StaffCountView[], limit = 6): string {
  if (list.length === 0) return ''
  const shown = list.slice(0, limit).map((s) => s.name)
  return shown.join('、') + (list.length > limit ? ' 等' : '')
}
</script>

<template>
  <aside class="sidebar">
    <section>
      <div class="sidebar__label">登記概況</div>
      <div class="sidebar__big">{{ totalCount }} 筆</div>
      <p class="sidebar__note">
        {{ activeStaffCount }} 人中 {{ activeStaffCount - noneList.length }} 人已登記 · 平均
        {{ activeStaffCount > 0 ? (totalCount / activeStaffCount).toFixed(1) : '0.0' }} 天 · 上限 {{ monthlyCap }} 天
      </p>
    </section>

    <section>
      <div class="sidebar__row">
        <span class="sidebar__label">已達上限</span>
        <span class="tag" :class="overList.length ? 'tag-accent' : 'tag-neutral'">{{ overList.length }} 人</span>
      </div>
      <p class="sidebar__note">
        {{ overList.length ? `${joinNames(overList, 4)}（已達上限，須先清除其他天）` : '尚無人達到上限' }}
      </p>
    </section>

    <section>
      <div class="sidebar__row">
        <span class="sidebar__label">尚未登記</span>
        <span class="tag tag-neutral">{{ noneList.length }} 人</span>
      </div>
      <p class="sidebar__note">
        {{ noneList.length ? `${joinNames(noneList)}——無通知機制，求解前逐一確認` : '全員皆已登記' }}
      </p>
    </section>

    <section>
      <div class="sidebar__label">可行性預警 · 逐日</div>
      <div v-if="shortageDays.length === 0" class="sidebar__note">目前沒有逐日缺口。</div>
      <div v-else class="risk-list">
        <div v-for="day in shortageDays" :key="day.date" class="risk-item" :class="{ 'risk-item--chief': day.hasChiefShortage }">
          <div class="risk-item__head">
            <span class="risk-item__date">{{ day.date }}</span>
            <span v-if="day.hasChiefShortage" class="tag tag-accent">總值缺口</span>
          </div>
          <div class="risk-item__detail">{{ day.text }}</div>
        </div>
      </div>
    </section>

    <section>
      <div class="sidebar__label">可行性預警 · 整月供需</div>
      <div class="layer-list">
        <div v-for="{ layer, ratio } in supplyLayers" :key="layer.areaTypeCodes.join(',')" class="layer">
          <div class="layer__head">
            <span class="layer__label">{{ layerLabel(layer.areaTypeCodes, areaTypeNameByCode) }}</span>
            <span class="layer__ratio" :class="{ 'layer__ratio--negative': (ratio.ratio ?? 1) < 1 }">
              {{ ratio.ratioLabel }}
            </span>
          </div>
          <div class="layer__track">
            <div class="layer__bar" :style="{ width: `${ratio.utilizationPercent}%` }" />
          </div>
          <div class="layer__note">
            需求 {{ layer.demandPoints }} · 供給 {{ layer.supplyPoints }}（額度點數）· 餘裕 {{ layer.headroom }}
          </div>
        </div>
      </div>
      <!-- 判斷依據見 logic.ts 的 evaluateWardSqueezeHint（ARCHITECTURE §9.1）；這裡只顯示結論，
           不在畫面文字裡帶內部文件章節號。 -->
      <div v-if="wardSqueezeHint?.show" class="ward-hint">
        低年級可值一般病房的剩餘供給吃緊（邊際供需比約 {{ wardSqueezeHint.ratio?.toFixed(2) }} 倍）：低年級登記越多，
        資深越被拉進 ICU 擠掉 R2/R3，「R2/R3 優先 ICU」這條偏好可能被犧牲。
      </div>
    </section>

    <section>
      <label class="sidebar__label" for="nb-cap">每人每月不可排班日上限</label>
      <input id="nb-cap" class="input" :value="monthlyCap" readonly />
    </section>
  </aside>
</template>

<style scoped>
.sidebar {
  flex: none;
  width: 300px;
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  border-left: 1px solid var(--color-divider);
  padding-left: var(--space-4);
}

.sidebar__label {
  font-family: var(--font-heading);
  font-weight: 600;
  font-size: 10px;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  margin-bottom: 6px;
  display: block;
}

.sidebar__row {
  display: flex;
  align-items: center;
  margin-bottom: 6px;
}

.sidebar__row .tag {
  margin-left: auto;
}

.sidebar__big {
  font-family: var(--font-heading);
  font-weight: 600;
  font-size: 30px;
}

.sidebar__note {
  margin: 3px 0 0;
  font-size: 11px;
  line-height: 1.5;
  color: color-mix(in srgb, var(--color-text) 58%, transparent);
}

.risk-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.risk-item {
  border: 1px solid color-mix(in srgb, var(--color-text) 12%, transparent);
  padding: 6px 8px;
}

.risk-item--chief {
  border-color: var(--color-accent);
  background: color-mix(in srgb, var(--color-accent) 8%, transparent);
}

.risk-item__head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.risk-item__date {
  font-family: var(--font-heading);
  font-weight: 600;
  font-size: 12.5px;
}

.risk-item__detail {
  margin-top: 2px;
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 58%, transparent);
}

.layer-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.layer__head {
  display: flex;
  align-items: baseline;
  gap: 6px;
}

.layer__label {
  font-family: var(--font-heading);
  font-weight: 600;
  font-size: 12.5px;
}

/* 倍率（供給是需求的幾倍）是主要視覺，「餘裕 N」降級到 `.layer__note` 當副文字（issue #53）。 */
.layer__ratio {
  margin-left: auto;
  font-size: 13px;
  font-weight: 600;
  font-family: ui-monospace, Menlo, monospace;
  color: var(--color-accent-800);
}

.layer__ratio--negative {
  color: var(--color-accent-900);
  background: color-mix(in srgb, var(--color-accent) 25%, transparent);
  padding: 1px 5px;
}

.layer__track {
  margin-top: 4px;
  height: 4px;
  background: color-mix(in srgb, var(--color-text) 9%, transparent);
}

.layer__bar {
  height: 4px;
  background: var(--color-accent);
}

.layer__note {
  margin-top: 3px;
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.ward-hint {
  margin-top: 9px;
  padding: 7px 8px;
  font-size: 10.5px;
  line-height: 1.5;
  border: 1px solid var(--color-accent);
  background: color-mix(in srgb, var(--color-accent) 8%, transparent);
  color: var(--color-accent-900);
}

.input {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--color-divider);
  background: var(--color-surface);
  font: inherit;
  color: var(--color-text);
}
</style>
