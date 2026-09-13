<script setup lang="ts">
/**
 * SCREEN 06 人員維護。設計稿 docs/design-ref/screen-06.html：左清單（搜尋 ／
 * 身分組 ／ 狀態篩選，全在前端做，`GET /staff` 一次全帶）＋ 右表單（新增／編輯／
 * 狀態切換／刪除）。純函式在 `staffList.ts`，這裡只接資料與畫面。
 */
import { computed, nextTick, reactive, ref } from 'vue'
import { Plus, Search } from 'lucide-vue-next'
import PageLayout from '@/components/PageLayout.vue'
import { useConfirm } from '@/composables/useConfirm'
import { invalidate, useResource } from '@/composables/useResource'
import { useYearMonth } from '@/composables/useYearMonth'
import { createStaff, deleteStaff, listStaff, setStaffStatus, updateStaff } from '@/api/staff'
import { getAreaSettings, getRankSettings } from '@/api/settings'
import { getPointBoard } from '@/api/views'
import { describeError } from '@/api/errors'
import type { Staff, StaffStatus, StaffWrite } from '@/api/types'
import {
  areaTypeChips,
  areaTypeNames,
  describeQuotaLoad,
  errorCodeOf,
  findPointBoardRow,
  groupCodeOfRank,
  groupNameOf,
  isNotFoundError,
  rankNameOf,
  staffCountsLabel,
  type GroupFilter,
  type StatusFilter,
  visibleStaff,
} from './staffList'

const { ym } = useYearMonth()

const staffKey = ref<string>('staff')
const ranksKey = ref<string>('settings/ranks')
const areasKey = ref<string>('settings/areas')
// 跟 SCREEN 01（`src/pages/schedule/index.vue`）用同一把 key，寫入排班表後這裡也會跟著失效重抓。
const pointBoardKey = computed(() => `schedules/${ym.value}/point-board`)

const staffResource = useResource(staffKey, () => listStaff())
const ranksResource = useResource(ranksKey, () => getRankSettings())
const areasResource = useResource(areasKey, () => getAreaSettings())
const pointBoardResource = useResource(pointBoardKey, () => getPointBoard(ym.value))

const ranks = computed(() => ranksResource.data.value?.ranks ?? [])
const groups = computed(() => ranksResource.data.value?.groups ?? [])
const areaTypes = computed(() => areasResource.data.value?.areaTypes ?? [])
const roster = computed(() => staffResource.data.value?.items ?? [])
const activeCount = computed(() => staffResource.data.value?.counts.active ?? 0)
const staffCounts = computed(() => staffResource.data.value?.counts ?? null)

const search = ref('')
const groupFilter = ref<GroupFilter>('all')
const statusFilter = ref<StatusFilter>('active')

const filteredStaff = computed(() =>
  visibleStaff(roster.value, ranks.value, groups.value, {
    search: search.value,
    group: groupFilter.value,
    status: statusFilter.value,
  }),
)

// --- 右側表單：draft 是獨立於快取的編輯狀態，不直接綁定 Staff。 ---

interface Draft {
  employeeNo: string
  name: string
  rankCode: string
}

function blankDraft(): Draft {
  return { employeeNo: '', name: '', rankCode: ranks.value[0]?.code ?? '' }
}

const selectedId = ref<string | null>(null)
const mode = ref<'idle' | 'create' | 'edit'>('idle')
const draft = reactive<Draft>(blankDraft())
const saving = ref(false)
const employeeNoError = ref<string | null>(null)
const formError = ref<string | null>(null)
const deleteNote = ref<string | null>(null)
const statusButtonEl = ref<HTMLButtonElement | null>(null)

const selectedStaff = computed<Staff | null>(
  () => roster.value.find((s) => s.id === selectedId.value) ?? null,
)

const selectedGroupCode = computed(() =>
  mode.value === 'idle' ? null : groupCodeOfRank(ranks.value, draft.rankCode),
)
const selectedGroupName = computed(() => groupNameOf(groups.value, selectedGroupCode.value))
const selectedAreaTypeChips = computed(() =>
  areaTypeChips(areaTypes.value, selectedStaff.value?.eligibleAreaTypes ?? []),
)

// 姓名旁的「本月 3/12 · 餘 9 · 假日 1 班」，只在編輯既有人員時顯示——新增中的人員
// 還沒有 staffId，點數看板查不到列。查無值班表（404）與其他載入失敗分開講，
// 避免使用者把「這個月還沒排」誤會成畫面壞了。
const quotaLoadText = computed(() => {
  if (mode.value !== 'edit' || !selectedStaff.value) return null
  if (pointBoardResource.error.value) {
    return isNotFoundError(pointBoardResource.error.value) ? '本月尚無值班表' : null
  }
  const groups = pointBoardResource.data.value?.groups
  if (!groups) return null
  return describeQuotaLoad(findPointBoardRow(groups, selectedStaff.value.id)) ?? '—'
})

function startCreate(): void {
  selectedId.value = null
  mode.value = 'create'
  Object.assign(draft, blankDraft())
  clearMessages()
}

function selectStaff(staff: Staff): void {
  selectedId.value = staff.id
  mode.value = 'edit'
  Object.assign(draft, {
    employeeNo: staff.employeeNo,
    name: staff.name,
    rankCode: staff.rankCode,
  })
  clearMessages()
}

function clearMessages(): void {
  employeeNoError.value = null
  formError.value = null
  deleteNote.value = null
}

function onEmployeeNoInput(): void {
  employeeNoError.value = null
}

const canSave = computed(
  () => draft.employeeNo.trim() !== '' && draft.name.trim() !== '' && draft.rankCode !== '' && !saving.value,
)

function toWrite(): StaffWrite {
  return {
    employeeNo: draft.employeeNo.trim(),
    name: draft.name.trim(),
    rankCode: draft.rankCode,
  }
}

async function save(): Promise<void> {
  if (!canSave.value) return
  saving.value = true
  formError.value = null
  employeeNoError.value = null
  try {
    const body = toWrite()
    const result = mode.value === 'create' ? await createStaff(body) : await updateStaff(selectedId.value as string, body)
    await invalidate('staff')
    selectedId.value = result.id
    mode.value = 'edit'
    Object.assign(draft, {
      employeeNo: result.employeeNo,
      name: result.name,
      rankCode: result.rankCode,
    })
  } catch (err) {
    if (errorCodeOf(err) === 'EMPLOYEE_NO_TAKEN') {
      employeeNoError.value = describeError(err)
    } else {
      formError.value = describeError(err)
    }
  } finally {
    saving.value = false
  }
}

async function toggleStatus(): Promise<void> {
  const staff = selectedStaff.value
  if (!staff) return
  const next: StaffStatus = staff.status === 'active' ? 'inactive' : 'active'
  saving.value = true
  formError.value = null
  try {
    await setStaffStatus(staff.id, { status: next })
    await invalidate('staff')
  } catch (err) {
    formError.value = describeError(err)
  } finally {
    saving.value = false
  }
}

async function remove(): Promise<void> {
  const staff = selectedStaff.value
  if (!staff) return
  const ok = await useConfirm().confirm({
    title: '刪除人員',
    message: `確定要刪除「${staff.name}」嗎？此動作無法復原。`,
    confirmText: '刪除',
    cancelText: '取消',
  })
  if (!ok) return

  saving.value = true
  deleteNote.value = null
  try {
    await deleteStaff(staff.id)
    await invalidate('staff')
    selectedId.value = null
    mode.value = 'idle'
    Object.assign(draft, blankDraft())
  } catch (err) {
    if (errorCodeOf(err) === 'STAFF_HAS_DUTIES') {
      deleteNote.value = describeError(err)
      saving.value = false
      await nextTick()
      statusButtonEl.value?.focus()
    } else {
      formError.value = describeError(err)
    }
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <PageLayout title="人員維護" :subtitle="`在職 ${activeCount} 人`">
    <template #actions>
      <button type="button" class="btn btn-primary" @click="startCreate">
        <Plus :size="16" :stroke-width="1.5" />新增人員
      </button>
    </template>

    <div class="staff-page">
      <section class="staff-list">
        <div class="staff-list__toolbar">
          <label class="staff-search">
            <Search :size="14" :stroke-width="1.5" />
            <input v-model="search" class="input" type="search" placeholder="搜尋姓名或員編" />
          </label>

          <div class="seg" role="group" aria-label="身分組篩選">
            <label class="seg-opt">
              <input v-model="groupFilter" type="radio" name="group-filter" value="all" />全部
            </label>
            <label v-for="group in groups" :key="group.code" class="seg-opt">
              <input v-model="groupFilter" type="radio" name="group-filter" :value="group.code" />{{ group.name }}
            </label>
          </div>

          <div class="seg" role="group" aria-label="狀態篩選">
            <label class="seg-opt">
              <input v-model="statusFilter" type="radio" name="status-filter" value="active" />在職
            </label>
            <label class="seg-opt">
              <input v-model="statusFilter" type="radio" name="status-filter" value="inactive" />停用
            </label>
            <label class="seg-opt">
              <input v-model="statusFilter" type="radio" name="status-filter" value="all" />全部
            </label>
          </div>

          <span v-if="staffCounts" class="staff-list__counts">{{ staffCountsLabel(staffCounts) }}</span>
        </div>

        <div v-if="staffResource.error.value" class="staff-list__message">
          {{ describeError(staffResource.error.value) }}
        </div>
        <div v-else-if="staffResource.loading.value && !staffResource.data.value" class="staff-list__message">
          載入中…
        </div>
        <div v-else class="staff-list__table">
          <div class="staff-row staff-row--head">
            <span class="staff-row__rank">身分</span>
            <span class="staff-row__group">組</span>
            <span class="staff-row__name">姓名</span>
            <span class="staff-row__emp">員編</span>
            <span class="staff-row__able">可值類型 · 唯讀</span>
            <span class="staff-row__status">狀態</span>
          </div>
          <div class="staff-list__body">
            <button
              v-for="staff in filteredStaff"
              :key="staff.id"
              type="button"
              class="staff-row"
              :class="{ 'staff-row--selected': staff.id === selectedId }"
              @click="selectStaff(staff)"
            >
              <span class="staff-row__rank"><span class="tag tag-accent">{{ rankNameOf(ranks, staff.rankCode) }}</span></span>
              <span class="staff-row__group">{{ groupNameOf(groups, groupCodeOfRank(ranks, staff.rankCode)) }}</span>
              <span class="staff-row__name">{{ staff.name }}</span>
              <span class="staff-row__emp">{{ staff.employeeNo }}</span>
              <span class="staff-row__able">{{ areaTypeNames(staff.eligibleAreaTypes, areaTypes) }}</span>
              <span class="staff-row__status">
                <span class="tag" :class="staff.status === 'active' ? 'tag-accent' : 'tag-neutral'">
                  {{ staff.status === 'active' ? '在職' : '停用' }}
                </span>
              </span>
            </button>
            <p v-if="filteredStaff.length === 0" class="staff-list__empty">沒有符合條件的人員。</p>
          </div>
        </div>
      </section>

      <section class="staff-form">
        <template v-if="mode === 'idle'">
          <p class="staff-form__placeholder">選擇左側人員以編輯，或按「新增人員」建立新資料。</p>
        </template>
        <template v-else>
          <div>
            <div class="k">{{ mode === 'create' ? '新增人員' : '編輯人員' }}</div>
            <div class="staff-form__title">{{ mode === 'create' ? '未命名' : selectedStaff?.name }}</div>
            <div v-if="quotaLoadText" class="staff-form__load">{{ quotaLoadText }}</div>
          </div>

          <p v-if="formError" class="staff-form__error">{{ formError }}</p>

          <div class="staff-form__row">
            <label class="field">
              <span>姓名</span>
              <input v-model="draft.name" class="input" type="text" placeholder="姓名" />
            </label>
            <label class="field field--narrow">
              <span>員編</span>
              <input
                v-model="draft.employeeNo"
                class="input"
                type="text"
                placeholder="員編"
                @input="onEmployeeNoInput"
              />
              <span v-if="employeeNoError" class="field__error">{{ employeeNoError }}</span>
            </label>
          </div>

          <div class="staff-form__row">
            <label class="field field--narrow">
              <span>身分</span>
              <select v-model="draft.rankCode" class="input">
                <option v-for="rank in ranks" :key="rank.code" :value="rank.code">{{ rank.name }}</option>
              </select>
            </label>
            <label class="field">
              <span>身分組 · 唯讀</span>
              <input class="input" type="text" :value="selectedGroupName" readonly />
            </label>
          </div>

          <div v-if="mode === 'edit' && selectedStaff">
            <div class="k">在職狀態 · 兩態、無生效日</div>
            <div class="status-toggle">
              <span :class="['status-toggle__pill', { 'status-toggle__pill--on': selectedStaff.status === 'active' }]"
                >在職</span
              >
              <span :class="['status-toggle__pill', { 'status-toggle__pill--on': selectedStaff.status === 'inactive' }]"
                >停用</span
              >
            </div>
            <p class="staff-form__hint">停用不影響歷史值班表；已有值班紀錄者無法刪除。</p>
          </div>

          <div v-if="mode === 'edit' && selectedStaff">
            <div class="staff-form__label-row">
              <span class="k">可值區域類型</span>
              <span class="staff-form__hint-inline">依資格矩陣自動推導</span>
            </div>
            <div class="chip-row">
              <span
                v-for="chip in selectedAreaTypeChips"
                :key="chip.code"
                class="tag"
                :class="chip.eligible ? 'tag-accent' : 'tag-neutral'"
              >
                {{ chip.name }}
              </span>
            </div>
            <p class="staff-form__hint">額度點數上限屬於身分（SCREEN 02），當月的值在點數看板。</p>
          </div>

          <div class="staff-form__actions">
            <button type="button" class="btn btn-primary btn-block" :disabled="!canSave" @click="save">
              儲存變更
            </button>
            <button
              v-if="mode === 'edit' && selectedStaff"
              ref="statusButtonEl"
              type="button"
              class="btn btn-secondary btn-block"
              :disabled="saving"
              @click="toggleStatus"
            >
              {{ selectedStaff.status === 'active' ? '停用' : '恢復在職' }}
            </button>
            <div v-if="mode === 'edit' && selectedStaff" class="staff-form__delete-row">
              <button type="button" class="staff-form__delete-link" :disabled="saving" @click="remove">
                刪除人員
              </button>
              <p class="staff-form__hint">已有值班紀錄，只能停用；刪除會回 409</p>
            </div>
            <p v-if="deleteNote" class="staff-form__error">{{ deleteNote }}</p>
          </div>
        </template>
      </section>
    </div>
  </PageLayout>
</template>

<style scoped>
/* Industry 的 .k（小標籤字樣），只存在於 docs/design-ref/industry.css，這裡用 token 補一份。 */
.k {
  font: 600 10px var(--font-heading);
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.staff-page {
  display: flex;
  height: 100%;
  min-height: 0;
  gap: var(--space-4);
}

.staff-list {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  border: 1px solid var(--color-divider);
  background: var(--color-bg);
}

.staff-list__toolbar {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-3);
  border-bottom: 1px solid var(--color-divider);
  flex-wrap: wrap;
}

.staff-search {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.staff-search .input {
  width: 190px;
}

.staff-list__counts {
  margin-left: auto;
  font-size: 11px;
  white-space: nowrap;
  color: color-mix(in srgb, var(--color-text) 52%, transparent);
}

.staff-list__table {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding: var(--space-3);
  gap: var(--space-2);
}

.staff-list__body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}

.staff-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  /* 列高對齊設計稿約 33px：主要靠 .staff-row__group 不換行，這裡的直向 padding 只需要
     很薄的一層（見下方 .staff-row__rank 的 .tag 已經有自己的內距）。 */
  padding: var(--space-1) var(--space-2);
  border: none;
  border-bottom: 1px solid color-mix(in srgb, var(--color-text) 7%, transparent);
  background: transparent;
  font: inherit;
  color: inherit;
  text-align: left;
  cursor: pointer;
  width: 100%;
}

.staff-row--head {
  cursor: default;
  font-family: var(--font-heading);
  font-size: 10px;
  letter-spacing: 0.04em;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  border-bottom: 1px solid var(--color-divider);
}

.staff-row:not(.staff-row--head):hover {
  background: color-mix(in srgb, var(--color-text) 4%, transparent);
}

.staff-row--selected {
  background: var(--color-accent-100);
}

.staff-row__rank {
  width: 52px;
  flex: none;
}

.staff-row__group {
  width: 48px;
  flex: none;
  white-space: nowrap;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.staff-row__name {
  width: 72px;
  flex: none;
  font-size: 12.5px;
}

.staff-row__emp {
  width: 64px;
  flex: none;
  font-family: ui-monospace, Menlo, monospace;
  font-variant-numeric: tabular-nums;
  font-size: 11px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.staff-row__able {
  flex: 1;
  min-width: 0;
  font-size: 11px;
  color: var(--color-accent-700);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.staff-row__status {
  margin-left: auto;
  flex: none;
}

.staff-list__empty,
.staff-list__message {
  padding: var(--space-4);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  text-align: center;
}

.staff-form {
  width: 314px;
  flex: none;
  border: 1px solid var(--color-divider);
  background: var(--color-bg);
  padding: var(--space-4);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  overflow-y: auto;
}

.staff-form__placeholder {
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
  font-size: 12.5px;
}

.staff-form__title {
  font-family: var(--font-heading);
  font-weight: var(--font-heading-weight);
  font-size: 19px;
}

.staff-form__load {
  font-size: 10.5px;
  color: color-mix(in srgb, var(--color-text) 52%, transparent);
  margin-top: 3px;
}

.staff-form__row {
  display: flex;
  gap: var(--space-2);
}

.field {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field span:first-child {
  font-size: 11px;
  color: color-mix(in srgb, var(--color-text) 70%, transparent);
}

.field--narrow {
  width: 108px;
  flex: none;
}

.field__error {
  font-size: 10.5px;
  color: var(--color-accent-900);
}

.input {
  min-height: 32px;
  padding: 0 var(--space-2);
  font-size: 14px;
  color: var(--color-text);
  background: var(--color-surface);
  border: 1px solid var(--color-divider);
  border-radius: var(--radius-md);
}

.input:focus-visible {
  border-color: var(--color-accent);
}

.status-toggle {
  display: flex;
  margin-top: var(--space-1);
}

.status-toggle__pill {
  flex: 1;
  text-align: center;
  padding: var(--space-2);
  font-size: 12px;
  border: 1px solid var(--color-divider);
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.status-toggle__pill--on {
  background: var(--color-accent);
  color: var(--color-bg);
  border-color: var(--color-accent);
}

.staff-form__label-row {
  display: flex;
  align-items: baseline;
  margin-bottom: 5px;
}

.staff-form__hint-inline {
  margin-left: auto;
  font-size: 10px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.chip-row {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.staff-form__hint {
  font-size: 10.5px;
  line-height: 1.5;
  color: color-mix(in srgb, var(--color-text) 52%, transparent);
  margin-top: var(--space-1);
}

.staff-form__error {
  font-size: 11.5px;
  color: var(--color-accent-900);
  background: var(--color-accent-100);
  padding: var(--space-2);
}

.staff-form__actions {
  margin-top: auto;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.btn-block {
  width: 100%;
}

.staff-form__delete-row {
  text-align: center;
}

.staff-form__delete-link {
  background: none;
  border: none;
  padding: 0;
  font-size: 12px;
  color: var(--color-accent-900);
  text-decoration: underline;
  cursor: pointer;
}

.staff-form__delete-link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.staff-form__delete-row .staff-form__hint {
  margin-top: 3px;
}

.seg {
  display: inline-flex;
  border: 1px solid var(--color-divider);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.seg-opt {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: var(--space-1) var(--space-2);
  font-size: 12px;
  cursor: pointer;
}

.seg-opt + .seg-opt {
  border-left: 1px solid var(--color-divider);
}

.seg-opt input {
  position: absolute;
  opacity: 0;
  pointer-events: none;
}

.seg-opt:has(input:checked) {
  background: var(--color-accent);
  color: var(--color-bg);
}
</style>
