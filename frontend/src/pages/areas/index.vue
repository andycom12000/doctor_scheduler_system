<script setup lang="ts">
/**
 * SCREEN 02 區域與點數（issue #28）。四份設定文件、一顆頁面級儲存鈕：
 * 載入 → 本地草稿（深拷貝）→ 儲存（只 PUT 真的改過的文件）→ invalidate('settings')。
 *
 * 區域與區域類型唯讀顯示（GET /settings/areas，H1_AREA_COVERAGE 讀這裡，這頁不改它）。
 * 10 身分表可編額度點數上限／點數類型；R6 的當月覆寫走 /settings/monthly-overrides/{ym}
 * （唯一有實際用途的覆寫對象，見 docs/constraint-defaults.md）。
 */
import { computed, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { CircleAlert, RotateCcw, Save } from 'lucide-vue-next'
import PageLayout from '@/components/PageLayout.vue'
import { useConfirm } from '@/composables/useConfirm'
import { invalidate, useResource } from '@/composables/useResource'
import { useYearMonth } from '@/composables/useYearMonth'
import { describeError } from '@/api/errors'
import {
  getAreaSettings,
  getEligibilityMatrix,
  getMonthlyOverride,
  getPointRules,
  getRankSettings,
  putMonthlyOverride,
  putPointRules,
  putRankSettings,
} from '@/api/settings'
import type { MonthlyOverride, PointRules, RankSettings } from '@/api/types'
import { areasOfType, cloneJson, eligibleAreaTypeNames, isEqualJson, isNonNegativeInteger, isPositiveInteger } from './logic'

const { ym } = useYearMonth()
const { confirm } = useConfirm()

const areasRes = useResource(computed(() => 'settings/areas'), () => getAreaSettings())
const ranksRes = useResource(computed(() => 'settings/ranks'), () => getRankSettings())
const pointRulesRes = useResource(computed(() => 'settings/point-rules'), () => getPointRules())
// 可值類型只顯示、不編輯——由資格矩陣推，改矩陣要去「資格與約束」頁。
const eligibilityRes = useResource(computed(() => 'settings/eligibility-matrix'), () => getEligibilityMatrix())
const overrideRes = useResource(
  computed(() => `settings/monthly-overrides/${ym.value}`),
  () => getMonthlyOverride(ym.value),
)

// 本地草稿：從各自的 useResource 深拷貝出來，成功儲存後 invalidate('settings') 重抓，
// 下面的 watch 會用新資料把草稿蓋回去（不加「只在未修改時同步」的條件——PUT 之後
// PutSettingsCommands 會正規化資料形狀，草稿若不跟著換，儲存鈕會卡在「亮著」）。
const ranksDraft = ref<RankSettings | null>(null)
const pointRulesDraft = ref<PointRules | null>(null)
const overrideDraft = ref<MonthlyOverride | null>(null)

watch(ranksRes.data, (value) => { ranksDraft.value = value ? cloneJson(value) : null }, { immediate: true })
watch(pointRulesRes.data, (value) => { pointRulesDraft.value = value ? cloneJson(value) : null }, { immediate: true })
watch(overrideRes.data, (value) => { overrideDraft.value = value ? cloneJson(value) : null }, { immediate: true })

const ranksDirty = computed(() => !isEqualJson(ranksDraft.value, ranksRes.data.value))
const pointRulesDirty = computed(() => !isEqualJson(pointRulesDraft.value, pointRulesRes.data.value))
const overrideDirty = computed(() => !isEqualJson(overrideDraft.value, overrideRes.data.value))
const dirty = computed(() => ranksDirty.value || pointRulesDirty.value || overrideDirty.value)

// R6 是目前唯一有當月覆寫用途的身分（docs/constraint-defaults.md）；其餘身分只顯示「—」。
const r6Override = computed<number | null>({
  get: () => overrideDraft.value?.quotaCapByRank?.R6 ?? null,
  set: (value) => {
    if (!overrideDraft.value) return
    if (value === null) {
      const capByRank = overrideDraft.value.quotaCapByRank
      if (capByRank) {
        delete capByRank.R6
        // 清空後若沒有其他身分的覆寫，整個欄位一併移除，草稿才會等於「未覆寫」的原始 GET 形狀，
        // 不然會卡在 { yearMonth, quotaCapByRank: {} } ≠ { yearMonth }，儲存鈕永遠亮著。
        if (Object.keys(capByRank).length === 0) delete overrideDraft.value.quotaCapByRank
      }
      return
    }
    overrideDraft.value.quotaCapByRank = { ...(overrideDraft.value.quotaCapByRank ?? {}), R6: value }
  },
})

function onR6OverrideInput(event: Event): void {
  const raw = (event.target as HTMLInputElement).value
  if (raw.trim() === '') {
    r6Override.value = null
    return
  }
  const parsed = Number(raw)
  r6Override.value = Number.isFinite(parsed) ? Math.trunc(parsed) : Number.NaN
}

const invalid = computed(() => {
  if (ranksDraft.value) {
    for (const rank of ranksDraft.value.ranks) {
      if (rank.code === 'NP') continue
      if (!isNonNegativeInteger(rank.quotaCap)) return true
    }
  }
  if (pointRulesDraft.value) {
    const { quota, fairness } = pointRulesDraft.value
    if (!isNonNegativeInteger(quota.weekday) || !isNonNegativeInteger(quota.holiday)) return true
    for (const rows of Object.values(fairness.tables)) {
      if (rows.some((row) => !isNonNegativeInteger(row.points))) return true
    }
    if (!isNonNegativeInteger(fairness.consecutiveSaturdayBonus.points)) return true
    if (!isPositiveInteger(fairness.consecutiveSaturdayBonus.windowDays)) return true
  }
  if (overrideDraft.value?.quotaCapByRank?.R6 !== undefined) {
    if (!isNonNegativeInteger(overrideDraft.value.quotaCapByRank.R6)) return true
  }
  return false
})

const saving = ref(false)
const saveError = ref<string | null>(null)

async function save(): Promise<void> {
  if (!dirty.value || invalid.value || saving.value) return
  saving.value = true
  saveError.value = null
  try {
    const tasks: Promise<unknown>[] = []
    if (ranksDirty.value && ranksDraft.value) tasks.push(putRankSettings(ranksDraft.value))
    if (pointRulesDirty.value && pointRulesDraft.value) tasks.push(putPointRules(pointRulesDraft.value))
    if (overrideDirty.value && overrideDraft.value) {
      tasks.push(putMonthlyOverride(ym.value, { ...overrideDraft.value, yearMonth: ym.value }))
    }
    await Promise.all(tasks)
  } catch (err) {
    saveError.value = describeError(err)
  } finally {
    await invalidate('settings')
    saving.value = false
  }
}

function discard(): void {
  ranksDraft.value = ranksRes.data.value ? cloneJson(ranksRes.data.value) : null
  pointRulesDraft.value = pointRulesRes.data.value ? cloneJson(pointRulesRes.data.value) : null
  overrideDraft.value = overrideRes.data.value ? cloneJson(overrideRes.data.value) : null
  saveError.value = null
}

onBeforeRouteLeave(async () => {
  if (!dirty.value) return true
  return confirm({
    title: '有未儲存的變更',
    message: '離開這一頁會捨棄尚未儲存的變更，確定要離開嗎？',
    confirmText: '離開',
    cancelText: '留在此頁',
  })
})

function groupName(groupCode: string): string {
  return ranksRes.data.value?.groups.find((group) => group.code === groupCode)?.name ?? groupCode
}
</script>

<template>
  <PageLayout title="區域與點數" subtitle="區域類型 3 種 · 區域 5 個 · 身分 10 種 · 兩套點數規則">
    <template #actions>
      <span v-if="saveError" class="areas__error"><CircleAlert :size="14" :stroke-width="1.5" />{{ saveError }}</span>
      <span v-else-if="invalid && dirty" class="areas__error">
        <CircleAlert :size="14" :stroke-width="1.5" />有欄位格式錯誤（需為非負整數），請修正後再試
      </span>
      <span v-else-if="dirty" class="tag tag-accent">有未儲存的變更</span>
      <button type="button" class="btn btn-secondary" :disabled="!dirty || saving" @click="discard">
        <RotateCcw :size="14" :stroke-width="1.5" />還原
      </button>
      <button type="button" class="btn btn-primary" :disabled="!dirty || invalid || saving" @click="save">
        <Save :size="14" :stroke-width="1.5" />{{ saving ? '儲存中…' : '儲存' }}
      </button>
    </template>

    <div class="areas">
      <section class="areas__section">
        <h2 class="areas__heading">區域類型與區域 <span class="areas__note">每區每日恰好 1 人，唯讀</span></h2>
        <div v-if="areasRes.data.value" class="areas__types">
          <div v-for="type in areasRes.data.value.areaTypes" :key="type.code" class="areas__type-row">
            <span class="tag tag-accent">{{ type.code }}</span>
            <span class="areas__type-name">{{ type.name }}</span>
            <div class="areas__type-areas">
              <span v-for="area in areasOfType(type.code, areasRes.data.value.areas)" :key="area.id" class="areas__area-chip">
                {{ area.code }}
              </span>
            </div>
          </div>
        </div>
        <p v-else-if="areasRes.loading.value">載入中…</p>
        <p v-else-if="areasRes.error.value" class="areas__error">{{ describeError(areasRes.error.value) }}</p>
      </section>

      <section class="areas__section">
        <h2 class="areas__heading">
          身分 10 種 · 身分組 4 組
          <span class="areas__note">身分組是公平性的比較單位，組內比剩餘額度</span>
        </h2>
        <table v-if="ranksDraft" class="table">
          <thead>
            <tr>
              <th>身分</th>
              <th>組</th>
              <th>可值類型 · 唯讀</th>
              <th>額度點數上限</th>
              <th>點數類型</th>
              <th>本月覆寫（{{ ym }}）</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="rank in ranksDraft.ranks" :key="rank.code">
              <td><span class="tag tag-accent">{{ rank.code }}</span></td>
              <td class="areas__group">{{ groupName(rank.groupCode) }}</td>
              <td class="areas__eligible">
                {{
                  eligibleAreaTypeNames(
                    eligibilityRes.data.value?.matrix ?? {},
                    rank.code,
                    areasRes.data.value?.areaTypes ?? [],
                  )
                }}
              </td>
              <td>
                <span v-if="rank.code === 'NP'" class="areas__np">不計</span>
                <input
                  v-else
                  v-model.number="rank.quotaCap"
                  type="number"
                  min="0"
                  step="1"
                  class="input input--num"
                  :class="{ 'input--invalid': !isNonNegativeInteger(rank.quotaCap) }"
                />
              </td>
              <td>
                <span v-if="rank.code === 'NP'" class="areas__np">不計</span>
                <select v-else v-model="rank.pointType" class="input input--select">
                  <option value="A">Type A</option>
                  <option value="B">Type B</option>
                </select>
              </td>
              <td>
                <div v-if="rank.code === 'R6'" class="areas__override">
                  <input
                    type="number"
                    min="0"
                    step="1"
                    class="input input--num"
                    :class="{ 'input--invalid': r6Override !== null && !isNonNegativeInteger(r6Override) }"
                    :value="r6Override ?? ''"
                    placeholder="沿用預設"
                    @input="onR6OverrideInput"
                  />
                  <span v-if="r6Override !== null" class="tag tag-accent">已覆寫</span>
                </div>
                <span v-else class="areas__dash">—</span>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else-if="ranksRes.loading.value || eligibilityRes.loading.value">載入中…</p>
        <p v-else-if="ranksRes.error.value" class="areas__error">{{ describeError(ranksRes.error.value) }}</p>
        <p v-else-if="eligibilityRes.error.value" class="areas__error">{{ describeError(eligibilityRes.error.value) }}</p>
      </section>

      <section class="areas__section">
        <h2 class="areas__heading">① 額度點數 QUOTA POINT · 硬上限</h2>
        <div v-if="pointRulesDraft" class="areas__fields">
          <label class="areas__field">
            <span>平日 1 班</span>
            <input v-model.number="pointRulesDraft.quota.weekday" type="number" min="0" step="1" class="input" />
          </label>
          <label class="areas__field">
            <span>假日 1 班</span>
            <input v-model.number="pointRulesDraft.quota.holiday" type="number" min="0" step="1" class="input" />
          </label>
          <label class="areas__field areas__field--wide">
            <span>假日認定</span>
            <input class="input" value="在行事曆設定 · 週六 週日 國定假日" readonly />
          </label>
        </div>
        <p v-else-if="pointRulesRes.loading.value">載入中…</p>
        <p v-else-if="pointRulesRes.error.value" class="areas__error">{{ describeError(pointRulesRes.error.value) }}</p>
        <p class="areas__note">「假日」＝週六、週日與國定假日；補班日視為平日、1 點。假日怎麼認定在行事曆逐日覆寫，不在這一頁。</p>
      </section>

      <section class="areas__section">
        <h2 class="areas__heading">② 公平性點數 FAIRNESS POINT · 實驗性</h2>
        <div v-if="pointRulesDraft" class="areas__fair">
          <div v-for="type in ['A', 'B']" :key="type" class="areas__fair-table">
            <div class="areas__fair-label">Type {{ type }}</div>
            <table class="table">
              <thead>
                <tr><th>當日</th><th>隔日</th><th>點數</th></tr>
              </thead>
              <tbody>
                <tr v-for="(row, index) in pointRulesDraft.fairness.tables[type] ?? []" :key="index">
                  <td>{{ row.today === 'holiday' ? '假日' : '平日' }}</td>
                  <td>{{ row.tomorrow === 'holiday' ? '假日' : '平日' }}</td>
                  <td>
                    <input
                      v-model.number="row.points"
                      type="number"
                      min="0"
                      step="1"
                      class="input input--num"
                      :class="{ 'input--invalid': !isNonNegativeInteger(row.points) }"
                    />
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
        <p class="areas__note">公平性點數只用於軟性目標，沒有上限；權重（含是否啟用）在「資格與約束」頁設定。</p>
      </section>

      <section class="areas__section">
        <h2 class="areas__heading">③ 連值兩個週六 加分</h2>
        <div v-if="pointRulesDraft" class="areas__fields">
          <label class="areas__field">
            <span>加分點數</span>
            <input
              v-model.number="pointRulesDraft.fairness.consecutiveSaturdayBonus.points"
              type="number"
              min="0"
              step="1"
              class="input"
            />
          </label>
          <label class="areas__field">
            <span>天數視窗</span>
            <input
              v-model.number="pointRulesDraft.fairness.consecutiveSaturdayBonus.windowDays"
              type="number"
              min="1"
              step="1"
              class="input"
            />
          </label>
          <label class="areas__field areas__field--wide">
            <span>喘息判定</span>
            <input class="input" value="視窗內是否有國定假日" readonly />
          </label>
        </div>
        <p class="areas__note">國定假日只用在這條規則：判斷連值的兩個週六之間有沒有喘息的機會。不含一般的週六與週日。</p>
      </section>
    </div>
  </PageLayout>
</template>

<style scoped>
.areas {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
  max-width: 960px;
}

.areas__section {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.areas__heading {
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
  font-size: 13px;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  margin: 0;
}

.areas__note {
  font-size: 11px;
  font-weight: 400;
  text-transform: none;
  letter-spacing: normal;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  margin-left: auto;
}

.areas__types {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.areas__type-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  border: 1px solid var(--color-divider);
  padding: var(--space-2) var(--space-3);
}

.areas__type-name {
  font-size: 12.5px;
  width: 78px;
  flex: none;
}

.areas__type-areas {
  display: flex;
  gap: var(--space-1);
  flex: 1;
}

.areas__area-chip {
  font: 600 10.5px 'Barlow Condensed', sans-serif;
  padding: 2px 6px;
  border: 1px solid var(--color-divider);
}

.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12.5px;
}

.table th {
  text-align: left;
  font: 600 10px/1.4 'Barlow Condensed', sans-serif;
  letter-spacing: 0.04em;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  border-bottom: 1px solid var(--color-divider);
  padding: var(--space-1) var(--space-2);
}

.table td {
  padding: var(--space-1) var(--space-2);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
  vertical-align: middle;
}

.areas__group {
  font: 600 12px 'Barlow Condensed', sans-serif;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.areas__eligible {
  font-size: 11.5px;
  color: var(--color-accent-700);
}

.areas__np {
  font-size: 11.5px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.areas__dash {
  color: color-mix(in srgb, var(--color-text) 40%, transparent);
}

.areas__override {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.input {
  font-family: var(--font-body);
  font-size: 12.5px;
  padding: 4px 8px;
  border: 1px solid var(--color-divider);
  border-radius: var(--radius-sm);
  background: var(--color-bg);
  color: var(--color-text);
  min-height: 28px;
}

.input:focus-visible {
  outline: 2px solid var(--color-accent);
  outline-offset: 1px;
}

.input--num {
  width: 84px;
  text-align: center;
}

.input--select {
  width: 96px;
}

.input--invalid {
  border-color: var(--color-accent-900);
  background: color-mix(in srgb, var(--color-accent-900) 8%, var(--color-bg));
}

.areas__fields {
  display: flex;
  gap: var(--space-3);
}

.areas__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 11px;
  color: color-mix(in srgb, var(--color-text) 60%, transparent);
  flex: 1;
}

.areas__field--wide {
  flex: 2;
}

.areas__fair {
  display: flex;
  gap: var(--space-4);
}

.areas__fair-table {
  flex: 1;
}

.areas__fair-label {
  font: 600 12px 'Barlow Condensed', sans-serif;
  letter-spacing: 0.06em;
  margin-bottom: 4px;
}

.areas__error {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--color-accent-900);
}
</style>
