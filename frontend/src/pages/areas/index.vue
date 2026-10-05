<script setup lang="ts">
/**
 * SCREEN 02 區域與點數（issue #28）。四份設定文件、一顆頁面級儲存鈕：
 * 載入 → 本地草稿（深拷貝）→ 儲存（只 PUT 真的改過的文件，`Promise.allSettled`，
 * 失敗的那份不 invalidate、草稿留著讓使用者重試）→ invalidate 成功的那幾份。
 *
 * 區域與區域類型唯讀顯示（GET /settings/areas，H1_AREA_COVERAGE 讀這裡，這頁不改它）。
 * 10 身分表可編額度點數上限／點數類型；R6 的當月覆寫走 /settings/monthly-overrides/{ym}
 * （唯一有實際用途的覆寫對象，見 docs/constraint-defaults.md；其餘身分若 API 端已經有值
 * 則唯讀顯示，沒有編輯入口）。
 *
 * 真後端 GET 一律回 `{ yearMonth, quotaCapByRank: {} }`，MSW mock 未覆寫時省略整個
 * `quotaCapByRank`；比較 dirty 與清空覆寫時都要透過 `normalizeOverride` 正規化，見 './logic'。
 *
 * 指定月份覆寫的年月（`overrideYm`）跟全域 `useYearMonth` 是兩回事（issue #45）：這頁沒有
 * `:ym` 路由參數，切全域年月會導到排班主表，逼使用者離開這頁才能編另一個月的 R6 覆寫。
 * `overrideYm` 只是一個獨立的本地 ref，預設抄 `useYearMonth()` 的目前年月，之後只由「指定
 * 月份覆寫」欄位旁的月份選擇器控制，不寫回路由、不影響 ranks／point-rules 這些跟月份無關的
 * 設定。選擇器是 `<select>` 不是 `<input type="month">`（PR #48 審查 B1）：Chromium 對
 * 多欄位輸入逐欄編輯時只要 `.value` 合法就發 `change`，年份從 2026 打成 2027 的過程會
 * 依序產生 0002-09、0020-09、0202-09 等中繼合法值，在草稿 dirty 時每一鍵都會彈一次確認、
 * 清空年份欄還會被回填搶輸入。候選清單與位移邏輯（`yearMonthOptions`／`shiftYearMonth`）
 * 跟 `YearMonthSwitcher.vue` 共用，定義在 `@/composables/useYearMonth`。
 */
import { computed, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { CircleAlert, Plus, RotateCcw, Save } from 'lucide-vue-next'
import PageLayout from '@/components/PageLayout.vue'
import { useConfirm } from '@/composables/useConfirm'
import { invalidate, useResource } from '@/composables/useResource'
import { shiftYearMonth, useYearMonth, yearMonthOptions } from '@/composables/useYearMonth'
import { describeError } from '@/api/errors'
import { describeSaveFailures } from '@/pages/saveReport'
import {
  getAreaSettings,
  getConstraints,
  getEligibilityMatrix,
  getMonthlyOverride,
  getPointRules,
  getRankSettings,
  putMonthlyOverride,
  putPointRules,
  putRankSettings,
} from '@/api/settings'
import type { MonthlyOverride, PointRules, RankSettings } from '@/api/types'
import {
  areasOfType,
  areaTypeCapacityNote,
  cloneJson,
  eligibleAreaTypeNames,
  isEqualJson,
  isNonNegativeInteger,
  isPositiveInteger,
  monthlyOverrideKey,
  normalizeOverride,
  s7Status,
} from './logic'

const { ym } = useYearMonth()
const { confirm } = useConfirm()

// 同一把 key 給「資格與約束」頁（`src/pages/constraints/index.vue`），那頁存檔成功會
// invalidate('settings/constraints')，這裡跟著失效重抓，S7 標籤不必自己另外處理。
const constraintsRes = useResource(computed(() => 'settings/constraints'), () => getConstraints())

// 「指定月份覆寫」的年月，獨立於全域 useYearMonth（見上方檔案註解），預設抄目前年月。
const overrideYm = ref(ym.value)

const areasRes = useResource(computed(() => 'settings/areas'), () => getAreaSettings())
const ranksRes = useResource(computed(() => 'settings/ranks'), () => getRankSettings())
const pointRulesRes = useResource(computed(() => 'settings/point-rules'), () => getPointRules())
// 可值類型只顯示、不編輯——由資格矩陣推，改矩陣要去「資格與約束」頁。
const eligibilityRes = useResource(computed(() => 'settings/eligibility-matrix'), () => getEligibilityMatrix())
const overrideRes = useResource(
  computed(() => monthlyOverrideKey(overrideYm.value)),
  () => getMonthlyOverride(overrideYm.value),
)

// 候選清單以全域目前年月為中心（不是 overrideYm——切到很久以前／以後的月份後，範圍不該
// 跟著遊走），overrideYm 本身若落在範圍外也要列進去，否則選單裡看不到目前選到的月份。
const overrideMonthOptions = computed(() => yearMonthOptions(ym.value, [overrideYm.value]))

// 本地草稿：從各自的 useResource 深拷貝出來，成功儲存後只 invalidate 那一份文件重抓
// （見 save()），下面的 watch 會用新資料把草稿蓋回去（不加「只在未修改時同步」的條件——
// PUT 之後 PutSettingsCommands 會正規化資料形狀，草稿若不跟著換，儲存鈕會卡在「亮著」）。
// 失敗的那份不 invalidate，草稿維持使用者剛才填的值不被蓋掉。
const ranksDraft = ref<RankSettings | null>(null)
const pointRulesDraft = ref<PointRules | null>(null)
const overrideDraft = ref<MonthlyOverride | null>(null)

watch(ranksRes.data, (value) => { ranksDraft.value = value ? cloneJson(value) : null }, { immediate: true })
watch(pointRulesRes.data, (value) => { pointRulesDraft.value = value ? cloneJson(value) : null }, { immediate: true })
watch(overrideRes.data, (value) => { overrideDraft.value = value ? cloneJson(value) : null }, { immediate: true })

const ranksDirty = computed(() => !isEqualJson(ranksDraft.value, ranksRes.data.value))
const pointRulesDirty = computed(() => !isEqualJson(pointRulesDraft.value, pointRulesRes.data.value))
// 真後端 GET 一律回 quotaCapByRank:{}，mock 未覆寫時省略欄位；比較前先正規化兩邊，
// 否則對真後端這裡會永遠判定「有變更」（見 normalizeOverride 註解）。
const overrideDirty = computed(() => !isEqualJson(normalizeOverride(overrideDraft.value), normalizeOverride(overrideRes.data.value)))
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
        // 清空後若沒有其他身分的覆寫，把整個欄位一併移除，草稿形狀盡量貼近「省略欄位」；
        // 但不能靠這個順手解決 dirty 判斷——真後端 GET 一律回 quotaCapByRank:{}，若草稿
        // 因為這裡而變成缺欄位，兩者仍然不 JSON-equal。真正的比較正規化在
        // overrideDirty（見上方，用 normalizeOverride 把「缺欄位」與「空物件」視為同一件事）。
        if (Object.keys(capByRank).length === 0) delete overrideDraft.value.quotaCapByRank
      }
      return
    }
    overrideDraft.value.quotaCapByRank = { ...(overrideDraft.value.quotaCapByRank ?? {}), R6: value }
  },
})

// 切月後的空窗期（新的 overrideYm 已生效，overrideRes 還在抓）overrideDraft 是 null，
// 這時仍打得動輸入框的話，使用者剛打的數字會被之後抓回來的資料覆寫、靜默丟掉（PR #48
// 審查 S1）。用佔位字樣說明目前是哪種空窗，而不是讓輸入框看起來只是「還沒填」。
const r6OverridePlaceholder = computed(() => {
  if (overrideDraft.value) return '沿用預設'
  if (overrideRes.loading.value) return '載入中…'
  return '無法載入'
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

interface SaveJob {
  label: string
  invalidateKey: string
  run: () => Promise<unknown>
}

async function save(): Promise<void> {
  if (!dirty.value || invalid.value || saving.value) return
  saving.value = true
  saveError.value = null
  try {
    const jobs: SaveJob[] = []
    if (ranksDirty.value && ranksDraft.value) {
      const draft = ranksDraft.value
      jobs.push({ label: '身分設定', invalidateKey: 'settings/ranks', run: () => putRankSettings(draft) })
    }
    if (pointRulesDirty.value && pointRulesDraft.value) {
      const draft = pointRulesDraft.value
      jobs.push({ label: '點數規則', invalidateKey: 'settings/point-rules', run: () => putPointRules(draft) })
    }
    if (overrideDirty.value && overrideDraft.value) {
      const draft = overrideDraft.value
      const targetYm = overrideYm.value
      jobs.push({
        label: `逐月覆寫 ${targetYm}`,
        invalidateKey: monthlyOverrideKey(targetYm),
        run: () => putMonthlyOverride(targetYm, { ...draft, yearMonth: targetYm }),
      })
    }

    const results = await Promise.allSettled(jobs.map((job) => job.run()))

    saveError.value = describeSaveFailures(
      results.map((result, index) => ({ label: jobs[index].label, result })),
      describeError,
    )

    // 只讓真的存成功的那幾份文件失效重抓；失敗的那份留著本地草稿，
    // 使用者剛填的值不會被 finally 重抓回來的舊資料蓋掉。
    await Promise.all(
      results
        .map((result, index) => ({ result, job: jobs[index] }))
        .filter((entry) => entry.result.status === 'fulfilled')
        .map((entry) => invalidate(entry.job.invalidateKey)),
    )
  } finally {
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

const s7 = computed(() => s7Status(constraintsRes.data.value ?? null))

/** 「＋ 新增類型」原型階段尚未開放完整流程（issue #55），只提示不落盤。 */
async function onAddAreaType(): Promise<void> {
  await confirm({
    title: '新增區域類型',
    message: '新增區域類型會影響資格矩陣與求解，原型階段尚未開放。',
    confirmText: '知道了',
    cancelText: '關閉',
  })
}

/**
 * 切換「指定月份覆寫」的年月。未儲存的覆寫草稿（`overrideDirty`，只看覆寫本身，
 * 不含 ranks／point-rules——那兩份跟月份無關）先問一次；取消時呼叫 `onCancelled`
 * 讓呼叫端把畫面上已經變了的顯示值改回來（見 `onOverrideMonthSelect`）。訊息明確寫
 * 「額度點數上限覆寫」，不能只寫「覆寫」或「點數」（CONTEXT.md：兩套點數不可混用，
 * 這裡指的是額度點數，不是公平性點數）。
 */
async function switchOverrideMonth(nextYm: string, onCancelled?: () => void): Promise<void> {
  if (nextYm === overrideYm.value) return
  if (overrideDirty.value) {
    const proceed = await confirm({
      title: '有未儲存的額度點數上限覆寫',
      message: `切換月份會捨棄「額度點數上限覆寫（${overrideYm.value}）」尚未儲存的變更，確定要切換到 ${nextYm} 嗎？`,
      confirmText: '切換',
      cancelText: '留在此月',
    })
    if (!proceed) {
      onCancelled?.()
      return
    }
  }
  overrideYm.value = nextYm
}

function shiftOverrideMonth(delta: number): void {
  void switchOverrideMonth(shiftYearMonth(overrideYm.value, delta))
}

// <select> 的 DOM 值在 change 事件當下已經被瀏覽器改成使用者選的選項；若使用者在
// confirm 對話框按取消，這裡要手動把 DOM 值改回 overrideYm，否則畫面會停在使用者選過
// 但其實沒生效的月份（overrideYm 沒變，Vue 對 :value 沒有變化的 patch 不保證會重繪）。
function onOverrideMonthSelect(event: Event): void {
  const select = event.target as HTMLSelectElement
  const value = select.value
  void switchOverrideMonth(value, () => { select.value = overrideYm.value })
}
</script>

<template>
  <PageLayout title="區域與點數" subtitle="區域類型 3 種 · 區域 5 個 · 身分 10 種 · 兩套點數規則">
    <template #actions>
      <span v-if="saveError" class="areas__error"><CircleAlert :size="14" :stroke-width="1.5" />{{ saveError }}</span>
      <span v-else-if="invalid && dirty" class="areas__error">
        <CircleAlert :size="14" :stroke-width="1.5" />有欄位格式錯誤（見下方標紅欄位），請修正後再試
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
        <h2 class="areas__heading">
          區域類型與區域
          <span class="areas__note">每區每日恰好 1 人，唯讀</span>
          <button type="button" class="btn btn-secondary areas__add-type" @click="onAddAreaType">
            <Plus :size="14" :stroke-width="1.5" />新增類型
          </button>
        </h2>
        <div v-if="areasRes.data.value" class="areas__types">
          <div v-for="type in areasRes.data.value.areaTypes" :key="type.code" class="areas__type-row">
            <span class="tag tag-accent">{{ type.code }}</span>
            <span class="areas__type-name">{{ type.name }}</span>
            <div class="areas__type-areas">
              <span v-for="area in areasOfType(type.code, areasRes.data.value.areas)" :key="area.id" class="areas__area-chip">
                {{ area.code }}
              </span>
            </div>
            <span class="areas__type-capacity">{{ areaTypeCapacityNote(type.code, areasRes.data.value.areas) }}</span>
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
              <th>
                <div class="areas__override-head">
                  <span>指定月份覆寫</span>
                  <span class="areas__ym-picker">
                    <button type="button" class="areas__ym-step" aria-label="覆寫月份：上一個月" :disabled="saving" @click="shiftOverrideMonth(-1)">‹</button>
                    <select
                      class="areas__ym-select"
                      :value="overrideYm"
                      aria-label="指定月份覆寫的年月"
                      :disabled="saving"
                      @change="onOverrideMonthSelect"
                    >
                      <option v-for="option in overrideMonthOptions" :key="option" :value="option">{{ option }}</option>
                    </select>
                    <button type="button" class="areas__ym-step" aria-label="覆寫月份：下一個月" :disabled="saving" @click="shiftOverrideMonth(1)">›</button>
                  </span>
                </div>
                <div v-if="overrideRes.error.value" class="areas__override-error">
                  <CircleAlert :size="12" :stroke-width="1.5" />載入失敗：{{ describeError(overrideRes.error.value) }}
                </div>
              </th>
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
                    :placeholder="r6OverridePlaceholder"
                    :disabled="saving || !overrideDraft"
                    @input="onR6OverrideInput"
                  />
                  <span v-if="r6Override !== null" class="tag tag-accent">已覆寫</span>
                </div>
                <!--
                  非 R6 身分沒有編輯入口（唯一有實際用途的覆寫對象只有 R6，見
                  docs/constraint-defaults.md），但 schema 上 quotaCapByRank 技術上可以放任何
                  身分代碼；若資料裡真的有值（例如手動打過 API），顯示出來而不是用「—」蓋掉，
                  避免看起來像沒有覆寫。
                -->
                <template v-else-if="overrideDraft?.quotaCapByRank?.[rank.code] !== undefined">
                  <span class="areas__override-readonly">{{ overrideDraft.quotaCapByRank[rank.code] }}</span>
                  <span class="tag tag-accent">已覆寫</span>
                </template>
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
            <input
              v-model.number="pointRulesDraft.quota.weekday"
              type="number"
              min="0"
              step="1"
              class="input"
              :class="{ 'input--invalid': !isNonNegativeInteger(pointRulesDraft.quota.weekday) }"
            />
          </label>
          <label class="areas__field">
            <span>假日 1 班</span>
            <input
              v-model.number="pointRulesDraft.quota.holiday"
              type="number"
              min="0"
              step="1"
              class="input"
              :class="{ 'input--invalid': !isNonNegativeInteger(pointRulesDraft.quota.holiday) }"
            />
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
        <h2 class="areas__heading">
          ② 公平性點數 FAIRNESS POINT · 實驗性
          <span v-if="s7" class="tag areas__s7-tag" :class="s7.enabled ? 'tag-accent' : 'tag-neutral'">{{ s7.label }}</span>
        </h2>
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
              :class="{ 'input--invalid': !isNonNegativeInteger(pointRulesDraft.fairness.consecutiveSaturdayBonus.points) }"
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
              :class="{ 'input--invalid': !isPositiveInteger(pointRulesDraft.fairness.consecutiveSaturdayBonus.windowDays) }"
            />
            <span v-if="!isPositiveInteger(pointRulesDraft.fairness.consecutiveSaturdayBonus.windowDays)" class="areas__field-error">
              至少 1 天
            </span>
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

.areas__type-capacity {
  flex: none;
  font-size: 11px;
  white-space: nowrap;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.areas__s7-tag {
  margin-left: auto;
  text-transform: none;
  letter-spacing: normal;
}

/* .btn 的預設尺寸是給獨立按鈕用的，在 13px 大寫標題列裡太大；縮小並取消繼承來的
   uppercase／letter-spacing，跟 .areas__note 對齊同一條基準線。 */
.areas__add-type {
  font-size: 11.5px;
  padding: 2px 8px;
  text-transform: none;
  letter-spacing: normal;
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

.areas__override-readonly {
  font: 600 12.5px 'Barlow Condensed', sans-serif;
  margin-right: var(--space-2);
}

.areas__override-head {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  text-transform: none;
  letter-spacing: normal;
}

.areas__ym-picker {
  display: flex;
  align-items: center;
  gap: 2px;
}

.areas__ym-step {
  width: 20px;
  height: 20px;
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: 1px solid var(--color-divider);
  color: var(--color-text);
  font: 400 13px/1 var(--font-heading);
  cursor: pointer;
}

.areas__ym-step:hover {
  background: color-mix(in srgb, var(--color-text) 7%, transparent);
}

.areas__ym-step:disabled,
.areas__ym-select:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.areas__ym-select {
  height: 22px;
  padding: 0 4px;
  font: 600 11px var(--font-body);
  letter-spacing: normal;
  text-transform: none;
  color: var(--color-text);
  background: var(--color-surface);
  border: 1px solid var(--color-divider);
}

.areas__override-error {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 4px;
  font-size: 10.5px;
  font-weight: 400;
  text-transform: none;
  letter-spacing: normal;
  color: var(--color-accent-900);
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

.areas__field-error {
  color: var(--color-accent-900);
  font-size: 10.5px;
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
  white-space: pre-line; /* 多份文件各自的失敗原因分行顯示 */
}
</style>
