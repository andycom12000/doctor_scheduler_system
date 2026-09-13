<script setup lang="ts">
/**
 * SCREEN 03 資格與約束（issue #28）。兩份設定文件、一顆頁面級儲存鈕：
 * 載入 → 本地草稿（深拷貝）→ 儲存（只 PUT 真的改過的文件，`Promise.allSettled`，
 * 失敗的那份不 invalidate、草稿留著讓使用者重試）→ invalidate 成功的那幾份。
 *
 * 矩陣 PUT 成功才 invalidate('settings/eligibility-matrix')（SCREEN 02 的「可值類型」欄
 * 跟著變）與 invalidate('staff')（可值區域類型由矩陣重推）；約束 PUT 成功才
 * invalidate('settings/constraints')——S7 權重改動影響 SCREEN 01／04 的公平性欄，
 * 那兩頁自己重抓 constraints，這裡不用特別處理。
 *
 * 硬約束只有 H2_ELIGIBILITY 給 enabled 開關（拍板照計畫 §3.3）：關 H1 求解器會回空表、
 * 關 H5 會把人排到不可排班日，一次點擊無確認就落盤風險太高，其餘 6 條唯讀顯示狀態。
 */
import { computed, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { CircleAlert, RotateCcw, Save } from 'lucide-vue-next'
import PageLayout from '@/components/PageLayout.vue'
import { useConfirm } from '@/composables/useConfirm'
import { invalidate, useResource } from '@/composables/useResource'
import { describeError } from '@/api/errors'
import {
  getAreaSettings,
  getConstraints,
  getEligibilityMatrix,
  getRankSettings,
  putConstraints,
  putEligibilityMatrix,
} from '@/api/settings'
import type { ConstraintSettings, EligibilityMatrix } from '@/api/types'
import {
  cloneJson,
  constraintBadge,
  describeDirection,
  describeHardConstraintParams,
  describeMetric,
  describeScope,
  isEqualJson,
  isValidWeight,
} from './logic'

// 右上「資格／硬約束／軟約束」三段切換：不拆頁、不改路由，純粹捲到對應區塊
// （PageLayout 的本體是捲動容器，`scrollIntoView` 不需要另外處理 offset）。
// 三個都是靜態 `ref="xxxSectionEl"`（SFC 編譯器認得的字面量寫法），不能用物件包起來——
// 動態 `:ref="expr"` 才能綁到巢狀路徑，這裡沒有必要多繞一手。
const matrixSectionEl = ref<HTMLElement | null>(null)
const hardSectionEl = ref<HTMLElement | null>(null)
const softSectionEl = ref<HTMLElement | null>(null)

type ConstraintSection = 'matrix' | 'hard' | 'soft'
function scrollToSection(section: ConstraintSection): void {
  const el = { matrix: matrixSectionEl, hard: hardSectionEl, soft: softSectionEl }[section].value
  el?.scrollIntoView({ block: 'start', behavior: 'smooth' })
}

const { confirm } = useConfirm()

const areasRes = useResource(computed(() => 'settings/areas'), () => getAreaSettings())
const ranksRes = useResource(computed(() => 'settings/ranks'), () => getRankSettings())
const matrixRes = useResource(computed(() => 'settings/eligibility-matrix'), () => getEligibilityMatrix())
const constraintsRes = useResource(computed(() => 'settings/constraints'), () => getConstraints())

const matrixDraft = ref<EligibilityMatrix | null>(null)
const constraintsDraft = ref<ConstraintSettings | null>(null)

watch(matrixRes.data, (value) => { matrixDraft.value = value ? cloneJson(value) : null }, { immediate: true })
watch(constraintsRes.data, (value) => { constraintsDraft.value = value ? cloneJson(value) : null }, { immediate: true })

const matrixDirty = computed(() => !isEqualJson(matrixDraft.value, matrixRes.data.value))
const constraintsDirty = computed(() => !isEqualJson(constraintsDraft.value, constraintsRes.data.value))
const dirty = computed(() => matrixDirty.value || constraintsDirty.value)

const invalid = computed(() => {
  if (constraintsDraft.value) {
    if (constraintsDraft.value.soft.some((soft) => !isValidWeight(soft.weight))) return true
  }
  return false
})

function toggleCell(rankCode: string, areaTypeCode: string): void {
  if (!matrixDraft.value) return
  const row = matrixDraft.value.matrix[rankCode] ?? (matrixDraft.value.matrix[rankCode] = {})
  row[areaTypeCode] = !row[areaTypeCode]
}

const saving = ref(false)
const saveError = ref<string | null>(null)

interface SaveJob {
  /** 成功時要 invalidate 的 key；矩陣多一個 'staff'（可值區域類型由矩陣重推）。 */
  invalidateKeys: string[]
  run: () => Promise<unknown>
}

async function save(): Promise<void> {
  if (!dirty.value || invalid.value || saving.value) return
  saving.value = true
  saveError.value = null
  try {
    const jobs: SaveJob[] = []
    if (matrixDirty.value && matrixDraft.value) {
      const draft = matrixDraft.value
      jobs.push({ invalidateKeys: ['settings/eligibility-matrix', 'staff'], run: () => putEligibilityMatrix(draft) })
    }
    if (constraintsDirty.value && constraintsDraft.value) {
      const draft = constraintsDraft.value
      jobs.push({ invalidateKeys: ['settings/constraints'], run: () => putConstraints(draft) })
    }

    const results = await Promise.allSettled(jobs.map((job) => job.run()))
    const firstFailure = results.find((result): result is PromiseRejectedResult => result.status === 'rejected')
    if (firstFailure) saveError.value = describeError(firstFailure.reason)

    // 只讓真的存成功的那幾份文件（與矩陣連帶的 staff）失效重抓；失敗的那份留著本地草稿，
    // 使用者剛改的內容不會被 finally 重抓回來的舊資料蓋掉。
    const keysToInvalidate = results
      .map((result, index) => ({ result, job: jobs[index] }))
      .filter((entry) => entry.result.status === 'fulfilled')
      .flatMap((entry) => entry.job.invalidateKeys)
    await Promise.all(keysToInvalidate.map((key) => invalidate(key)))
  } finally {
    saving.value = false
  }
}

function discard(): void {
  matrixDraft.value = matrixRes.data.value ? cloneJson(matrixRes.data.value) : null
  constraintsDraft.value = constraintsRes.data.value ? cloneJson(constraintsRes.data.value) : null
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
</script>

<template>
  <PageLayout title="資格與約束" subtitle="10 身分 × 3 區域類型資格矩陣 · 硬約束 7 條 · 軟約束 7 條">
    <template #actions>
      <div class="constraints__jump" role="group" aria-label="捲到對應區塊，不會切換畫面內容">
        <button type="button" class="constraints__jump-link" @click="scrollToSection('matrix')">↓ 資格</button>
        <button type="button" class="constraints__jump-link" @click="scrollToSection('hard')">↓ 硬約束</button>
        <button type="button" class="constraints__jump-link" @click="scrollToSection('soft')">↓ 軟約束</button>
      </div>
      <span v-if="saveError" class="constraints__error"><CircleAlert :size="14" :stroke-width="1.5" />{{ saveError }}</span>
      <span v-else-if="invalid && dirty" class="constraints__error">
        <CircleAlert :size="14" :stroke-width="1.5" />權重需為 0–100 的整數，請修正後再試
      </span>
      <span v-else-if="dirty" class="tag tag-accent">有未儲存的變更</span>
      <button type="button" class="btn btn-secondary" :disabled="!dirty || saving" @click="discard">
        <RotateCcw :size="14" :stroke-width="1.5" />還原
      </button>
      <button type="button" class="btn btn-primary" :disabled="!dirty || invalid || saving" @click="save">
        <Save :size="14" :stroke-width="1.5" />{{ saving ? '儲存中…' : '儲存' }}
      </button>
    </template>

    <div class="constraints">
      <section ref="matrixSectionEl" class="constraints__section">
        <h2 class="constraints__heading">
          身分 × 區域類型 資格
          <span class="constraints__note">案主給定的表，無推導規則；改了這裡，SCREEN 02 的「可值類型」欄跟著變</span>
        </h2>
        <table v-if="matrixDraft && ranksRes.data.value && areasRes.data.value" class="table constraints__matrix">
          <thead>
            <tr>
              <th></th>
              <th v-for="type in areasRes.data.value.areaTypes" :key="type.code" class="constraints__matrix-type">
                {{ type.code }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="rank in ranksRes.data.value.ranks" :key="rank.code">
              <td><span class="tag tag-accent">{{ rank.code }}</span></td>
              <td v-for="type in areasRes.data.value.areaTypes" :key="type.code" class="constraints__matrix-cell">
                <button
                  type="button"
                  class="constraints__checkbox"
                  :class="{ 'constraints__checkbox--on': matrixDraft.matrix[rank.code]?.[type.code] }"
                  :aria-pressed="!!matrixDraft.matrix[rank.code]?.[type.code]"
                  :aria-label="`${rank.code} 可否值 ${type.name}`"
                  @click="toggleCell(rank.code, type.code)"
                >
                  {{ matrixDraft.matrix[rank.code]?.[type.code] ? '✓' : '' }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else-if="matrixRes.loading.value || ranksRes.loading.value || areasRes.loading.value">載入中…</p>
        <p v-else-if="matrixRes.error.value" class="constraints__error">{{ describeError(matrixRes.error.value) }}</p>
        <p v-else-if="ranksRes.error.value" class="constraints__error">{{ describeError(ranksRes.error.value) }}</p>
        <p v-else-if="areasRes.error.value" class="constraints__error">{{ describeError(areasRes.error.value) }}</p>
      </section>

      <section ref="hardSectionEl" class="constraints__section">
        <h2 class="constraints__heading">
          硬約束 HARD · 7 條 · 違反即無解
          <span class="constraints__note">H4 · H7 跨月：讀上月末幾天為固定輸入</span>
        </h2>
        <div v-if="constraintsDraft" class="constraints__list">
          <div v-for="hard in constraintsDraft.hard" :key="hard.code" class="constraints__row">
            <span class="constraints__badge">{{ constraintBadge(hard.code) }}</span>
            <div class="constraints__name">
              <div>{{ hard.name }}</div>
              <div class="constraints__code">{{ hard.code }}</div>
            </div>
            <span class="constraints__scope">{{ describeScope(hard.scope) }}</span>
            <span class="constraints__params">{{ describeHardConstraintParams(hard.metric, hard.params) }}</span>
            <span class="tag tag-accent constraints__primitive">{{ hard.primitive }}</span>
            <label v-if="hard.code === 'H2_ELIGIBILITY'" class="constraints__toggle">
              <input type="checkbox" v-model="hard.enabled" />
              <span>{{ hard.enabled ? '已啟用' : '已停用' }}</span>
            </label>
            <!--
              只有 H2_ELIGIBILITY 給開關（拍板照計畫 §3.3）：關 H1 求解器會回空表、
              關 H5 會把人排到不可排班日，其餘 6 條唯讀顯示目前狀態，不能一次點擊無確認就落盤。
            -->
            <span v-else class="constraints__toggle constraints__toggle--readonly">
              {{ hard.enabled ? '已啟用' : '已停用' }}
            </span>
          </div>
        </div>
        <p v-else-if="constraintsRes.loading.value">載入中…</p>
        <p v-else-if="constraintsRes.error.value" class="constraints__error">{{ describeError(constraintsRes.error.value) }}</p>
        <p class="constraints__note constraints__note--block">
          H1 在求解器裡建成極高權重的軟項：登記過多時回「空缺最少」的變體而不是無解；在驗證層仍是硬違規。
        </p>
      </section>

      <section ref="softSectionEl" class="constraints__section">
        <h2 class="constraints__heading">
          軟約束 SOFT · 7 條 · 權重
          <span class="constraints__note">「避開」只顯示方向，不顯示負權重</span>
        </h2>
        <div v-if="constraintsDraft" class="constraints__list">
          <div v-for="soft in constraintsDraft.soft" :key="soft.code" class="constraints__row">
            <span class="constraints__badge">{{ constraintBadge(soft.code) }}</span>
            <div class="constraints__name">
              <div>{{ soft.name }}</div>
              <div class="constraints__code">{{ soft.code }}</div>
            </div>
            <span class="constraints__scope">{{ describeScope(soft.scope) }}</span>
            <span class="constraints__direction">{{ describeDirection(soft.params) ?? describeMetric(soft.metric) ?? '' }}</span>
            <span v-if="soft.weight === 0" class="tag tag-neutral">停用</span>
            <input
              v-model.number="soft.weight"
              type="number"
              min="0"
              max="100"
              step="1"
              class="input constraints__weight"
              :class="{ 'input--invalid': !isValidWeight(soft.weight) }"
            />
          </div>
        </div>
        <p class="constraints__note constraints__note--block">
          權重範圍 0–100，求解時自動正規化；填 0 等於停用該條軟約束。跨月補償不是獨立條目，是 S1 額度點數公平的起始偏移。
        </p>
      </section>
    </div>
  </PageLayout>
</template>

<style scoped>
.constraints {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
  max-width: 960px;
}

.constraints__section {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  /* 捲動目標貼齊視窗頂端會被固定的頁首擋住一截，留一點餘裕。 */
  scroll-margin-top: var(--space-4);
}

.constraints__heading {
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
  font-size: 13px;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  margin: 0;
}

.constraints__note {
  font-size: 11px;
  font-weight: 400;
  text-transform: none;
  letter-spacing: normal;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  margin-left: auto;
}

.constraints__note--block {
  margin: 0;
  margin-left: 0;
}

.table {
  border-collapse: collapse;
  font-size: 12.5px;
}

.table th,
.table td {
  padding: var(--space-1) var(--space-2);
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
}

.constraints__matrix-type {
  text-align: center;
  font: 600 11px 'Barlow Condensed', sans-serif;
  min-width: 64px;
}

.constraints__matrix-cell {
  text-align: center;
}

.constraints__checkbox {
  width: 24px;
  height: 24px;
  border: 1px solid var(--color-divider);
  background: var(--color-bg);
  color: var(--color-accent-900);
  font: 700 12px sans-serif;
  cursor: pointer;
}

.constraints__checkbox--on {
  background: var(--color-accent-200);
  border-color: var(--color-accent);
}

.constraints__list {
  display: flex;
  flex-direction: column;
}

.constraints__row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) 0;
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
}

.constraints__badge {
  flex: none;
  width: 28px;
  font: 600 11px 'Barlow Condensed', sans-serif;
  letter-spacing: 0.04em;
  color: var(--color-accent-700);
}

.constraints__name {
  flex: 1;
  min-width: 0;
  font-size: 12px;
}

.constraints__code {
  font: 600 9px/1.4 ui-monospace, Menlo, monospace;
  color: color-mix(in srgb, var(--color-text) 42%, transparent);
}

.constraints__scope {
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 58%, transparent);
  width: 160px;
  flex: none;
}

.constraints__params,
.constraints__direction {
  font: 600 10.5px 'Barlow Condensed', 'Noto Sans TC', sans-serif;
  color: color-mix(in srgb, var(--color-text) 62%, transparent);
  width: 96px;
  flex: none;
}

.constraints__primitive {
  flex: none;
}

.constraints__toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
  flex: none;
  width: 72px;
}

.constraints__toggle--readonly {
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
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

.input--invalid {
  border-color: var(--color-accent-900);
  background: color-mix(in srgb, var(--color-accent-900) 8%, var(--color-bg));
}

.constraints__weight {
  width: 60px;
  text-align: center;
  flex: none;
  font: 600 12px 'Barlow Condensed', sans-serif;
}

.constraints__error {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--color-accent-900);
}

/*
  故意不用 .seg／.seg-opt 那種帶框線分格的樣式——這三顆是「跳到」不是「切換」，
  頁面內容不會因為點了哪顆而改變，做成分格按鈕會讓人誤以為在切換分頁／篩選。
  這裡改成一組底線連結＋↓ 箭頭，視覺上就是錨點捲動。
*/
.constraints__jump {
  display: inline-flex;
  gap: var(--space-3);
}

.constraints__jump-link {
  padding: 0;
  font-size: 12px;
  background: transparent;
  border: none;
  color: var(--color-accent-900);
  text-decoration: underline;
  text-underline-offset: 2px;
  cursor: pointer;
}

.constraints__jump-link:hover {
  color: var(--color-accent);
}
</style>
