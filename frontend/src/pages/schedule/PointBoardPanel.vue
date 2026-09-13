<script setup lang="ts">
/**
 * 點數看板：依身分組分區，主欄是額度點數（已排／上限、剩餘、假日班）。
 * 公平性點數欄只在 S7（`S7_FAIRNESS_POINT`）權重 > 0 時顯示。
 * 「由上月帶入」直接用 `carryOverApplied`——這是公平性計算的起始偏移，跟額度點數是兩套點數
 * （CONTEXT.md），文案與 title 都要講清楚，不能寫成「前月+N」讓人誤會是額度加成。
 * NP 的 `quotaCap` 是 null（不計額度），改印 H6（`H6_NP_MONTHLY_DAYS`）的天數上限。
 */
import type { PointBoardGroup } from '@/api/types'
import { memberRankCodes } from './lib/pointBoardStats'

defineProps<{
  groups: PointBoardGroup[]
  fairOn: boolean
  published: boolean
  /** `H6_NP_MONTHLY_DAYS.params.cap`；讀不到時是 null，NP 那欄退回只顯示已值天數。 */
  npDutyCap: number | null
}>()

function percentOf(points: number, cap: number | null): number {
  if (cap === null || cap <= 0) return 0
  return Math.min(100, Math.round((points / cap) * 100))
}
</script>

<template>
  <section class="point-board">
    <div class="point-board__title">
      <span class="k">點數看板 · 額度點數（已排 / 上限 · 剩餘 · 假日班）</span>
      <span class="point-board__hint">
        依身分組分區 · 組內比較的是剩餘額度 ·
        {{ published ? '由上月帶入：發布時凍結' : '由上月帶入：草稿即時讀上月' }}
      </span>
    </div>
    <div class="point-board__cols">
      <div v-for="group in groups" :key="group.groupCode" class="point-board__col">
        <div class="point-board__col-head" :title="`${group.groupName} · ${memberRankCodes(group)}`">
          {{ group.groupName }} · {{ memberRankCodes(group) }}
        </div>
        <div v-for="row in group.rows" :key="row.staffId" class="point-board__row">
          <span class="point-board__rank">{{ row.rankCode }}</span>
          <span class="point-board__name">{{ row.name }}</span>
          <div class="point-board__bar-track">
            <div
              class="point-board__bar"
              :class="{ 'point-board__bar--full': percentOf(row.quotaPoints, row.quotaCap) >= 95 }"
              :style="{ width: `${percentOf(row.quotaPoints, row.quotaCap)}%` }"
            />
          </div>
          <span class="point-board__pts">
            {{
              row.quotaCap === null
                ? npDutyCap !== null
                  ? `${row.duties}/${npDutyCap} 天`
                  : `${row.duties} 天`
                : `${row.quotaPoints}/${row.quotaCap}`
            }}
          </span>
          <span class="point-board__left">{{ row.quotaRemaining === null ? '不計' : `餘 ${row.quotaRemaining}` }}</span>
          <span class="point-board__holiday">假{{ row.holidayDuties }}</span>
          <span v-if="fairOn" class="point-board__fair">公{{ row.fairnessPoints ?? '—' }}</span>
          <span
            v-if="row.carryOverApplied"
            class="point-board__carry"
            title="公平性計算的起始偏移，不計入額度點數"
          >由上月帶入 +{{ row.carryOverApplied }}</span>
        </div>
      </div>
    </div>
    <p v-if="!fairOn" class="point-board__note">公平性點數（S7）預設停用，整欄隱藏；啟用後成為看板的次要欄位。</p>
  </section>
</template>

<style scoped>
.k {
  font: 600 10px/1 var(--font-heading);
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.point-board {
  border-top: 1px solid var(--color-divider);
  padding-top: var(--space-3);
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.point-board__title {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.point-board__hint {
  margin-left: auto;
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.point-board__cols {
  display: flex;
  gap: var(--space-4);
  align-items: flex-start;
}

.point-board__col {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.point-board__col-head {
  font: 600 9.5px var(--font-heading);
  letter-spacing: 0.06em;
  padding: 3px 5px;
  background: var(--color-accent-100);
  color: var(--color-accent-800);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.point-board__row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 1px 3px;
  /* 欄很窄時（很多分組並排）允許換行——「由上月帶入」註記換到自己的整行，
     不要擠進姓名欄逼它換行（審查回饋 V2）。 */
  flex-wrap: wrap;
}

.point-board__rank {
  flex: none;
  width: 34px;
  text-align: center;
  font: 600 9px var(--font-heading);
  background: var(--color-accent-100);
  color: var(--color-accent-800);
  padding: 1px 2px;
}

.point-board__name {
  flex: none;
  width: 46px;
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.point-board__bar-track {
  flex: 1;
  height: 5px;
  background: color-mix(in srgb, var(--color-text) 9%, transparent);
}

.point-board__bar {
  height: 5px;
  background: var(--color-accent);
}

.point-board__bar--full {
  background: repeating-linear-gradient(45deg, var(--color-accent-800) 0 3px, var(--color-accent-400) 3px 6px);
}

.point-board__pts {
  flex: none;
  width: 40px;
  text-align: right;
  font: 600 10.5px var(--font-heading);
}

.point-board__left {
  flex: none;
  width: 34px;
  text-align: right;
  font: 600 9.5px var(--font-heading);
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.point-board__holiday {
  flex: none;
  width: 22px;
  text-align: right;
  font-size: 9.5px;
  color: color-mix(in srgb, var(--color-text) 45%, transparent);
}

.point-board__fair {
  flex: none;
  width: 32px;
  text-align: right;
  font-size: 9.5px;
  color: color-mix(in srgb, var(--color-text) 38%, transparent);
}

.point-board__carry {
  /* 獨佔一整行，跟姓名欄搶位置不會逼它換行（審查回饋 V2）；縮排對齊姓名欄起始位置。 */
  flex: 0 0 100%;
  padding-left: 40px;
  font-size: 9px;
  color: var(--color-accent-800);
}

.point-board__note {
  margin: 0;
  font-size: 10.5px;
  line-height: 1.5;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}
</style>
