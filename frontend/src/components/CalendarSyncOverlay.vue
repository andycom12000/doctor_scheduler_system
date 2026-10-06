<script setup lang="ts">
/**
 * 行事曆自動更新的全畫面遮罩與「未能更新」提醒（#112）。狀態機在
 * `composables/useCalendarSyncNotice.ts`，這裡只負責呈現：
 * 同步中不准任何操作；寫入失敗只能重試；取不到資料可以重試或先用現有資料。
 */
import { computed, onMounted } from 'vue'
import { calendarSyncGate, failureDetail, isLocked } from '@/composables/useCalendarSyncNotice'

const gate = calendarSyncGate
const locked = computed(() => isLocked(gate.phase.value))
const busy = computed(() => gate.phase.value === 'running')
const detail = computed(() => (gate.status.value ? failureDetail(gate.status.value) : ''))

onMounted(() => void gate.start())
</script>

<template>
  <div
    v-if="locked"
    class="sync-overlay screen-only"
    :class="{ 'sync-overlay--silent': gate.phase.value === 'checking' }"
    role="alertdialog"
    aria-modal="true"
    aria-labelledby="sync-overlay-title"
    :aria-busy="busy"
  >
    <!-- 剛開、還沒拿到狀態：前幾秒完全透明（不閃畫面），拖久了才浮出文字，不讓人對著空白乾等 -->
    <div v-if="gate.phase.value === 'checking'" class="sync-overlay__card sync-overlay__card--late">
      <h2 id="sync-overlay-title" class="sync-overlay__title">正在確認行事曆狀態…</h2>
      <p class="sync-overlay__text">請稍候。</p>
    </div>
    <div v-else class="sync-overlay__card">
      <template v-if="gate.phase.value === 'running'">
        <h2 id="sync-overlay-title" class="sync-overlay__title">正在更新行事曆…</h2>
        <p class="sync-overlay__text">正在從人事行政總處取得最新的國定假日與補班日，更新期間無法操作，請稍候。</p>
      </template>
      <template v-else-if="gate.phase.value === 'writeFailed'">
        <h2 id="sync-overlay-title" class="sync-overlay__title">行事曆更新失敗</h2>
        <p class="sync-overlay__text">
          已取得官方資料，但寫入時出了問題。{{ detail }}請重試；若一直失敗，請把程式資料夾旁
          <code>data/calendar-sync.log</code> 交給維護者。
        </p>
        <div class="sync-overlay__actions">
          <button type="button" class="btn btn-primary" @click="gate.retry()">重試</button>
        </div>
      </template>
      <template v-else>
        <h2 id="sync-overlay-title" class="sync-overlay__title">行事曆未能更新</h2>
        <p class="sync-overlay__text">
          取不到可用的官方資料（可能是網路不通、被擋下或來源異常）。{{ detail }}
          可以檢查網路後重試，或先用現有的行事曆資料繼續（國定假日可能不是最新），下次開啟程式會再試一次。
        </p>
        <div class="sync-overlay__actions">
          <button type="button" class="btn btn-secondary" @click="gate.continueWithStale()">先用現有資料</button>
          <button type="button" class="btn btn-primary" @click="gate.retry()">重試</button>
        </div>
      </template>
    </div>
  </div>

  <!-- 在頂部列下方佔位（不是 fixed）：不會蓋住導覽 -->
  <div v-if="gate.phase.value === 'stale'" class="sync-banner screen-only" role="status">
    <span>行事曆未能更新，國定假日可能不是最新。</span>
    <button type="button" class="btn btn-secondary sync-banner__retry" @click="gate.retry()">重試</button>
  </div>
</template>

<style scoped>
.sync-overlay {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, var(--color-neutral-900) 50%, transparent);
}

/* 還沒拿到第一次狀態：擋操作但不閃畫面 */
.sync-overlay--silent {
  background: transparent;
}

.sync-overlay__card {
  width: min(460px, calc(100vw - var(--space-8)));
  padding: var(--space-4);
  border: 1px solid var(--color-divider);
  border-radius: var(--radius-lg);
  background: var(--color-surface);
  box-shadow: var(--shadow-lg);
}

.sync-overlay__card--late {
  animation: sync-late-in 0.3s ease 3s both;
}

@keyframes sync-late-in {
  from {
    opacity: 0;
  }

  to {
    opacity: 1;
  }
}

.sync-overlay__title {
  margin: 0 0 var(--space-2);
  font-family: var(--font-heading);
  font-weight: var(--font-heading-weight);
  font-size: 20px;
}

.sync-overlay__text {
  margin: 0;
  font-size: 14px;
  opacity: 0.85;
}

.sync-overlay__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-4);
}

.sync-banner {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-3);
  padding: var(--space-1) var(--space-4);
  font-size: 13px;
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-divider);
}

.sync-banner__retry {
  padding-block: 0;
}
</style>
