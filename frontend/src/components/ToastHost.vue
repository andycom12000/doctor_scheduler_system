<script setup lang="ts">
/**
 * 全站共用的 toast 容器，掛在 App.vue 裡，固定在畫面右下角、不擋操作。
 * 外觀延續 `pages/blockedDays/index.vue` 的 `.toast`（accent 細框＋淡底、文字加深、右側「關閉」）。
 * 一般訊息區是常駐的 `aria-live="polite"`（每則 `role="status"`），錯誤區是常駐的 `role="alert"`；錯誤用實底色與較粗的框，跟一般訊息一眼可辨。
 * 狀態與計時在 `useToast.ts`。
 */
import { computed } from 'vue'
import { dismissToast, useToastList } from '@/composables/useToast'

const toasts = useToastList()
const infos = computed(() => toasts.value.filter((t) => t.kind === 'info'))
const errors = computed(() => toasts.value.filter((t) => t.kind === 'error'))
</script>

<template>
  <div class="toast-host">
    <!-- 兩個常駐的 live region，內容在裡面增減（動態才插入的 live region 有些讀屏器不會唸）。 -->
    <div class="toast-host__region" aria-live="polite">
      <div v-for="toast in infos" :key="toast.id" class="toast toast--info" role="status">
        <span class="toast__text">{{ toast.text }}</span>
        <button type="button" class="toast__close" @click="dismissToast(toast.id)">關閉</button>
      </div>
    </div>
    <div class="toast-host__region" role="alert">
      <div v-for="toast in errors" :key="toast.id" class="toast toast--error">
        <span class="toast__text">{{ toast.text }}</span>
        <button type="button" class="toast__close" @click="dismissToast(toast.id)">關閉</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 容器本身不吃滑鼠事件，只有每則 toast 吃，這樣不會蓋住底下的按鈕。 */
.toast-host {
  position: fixed;
  right: var(--space-4);
  bottom: var(--space-4);
  z-index: 50;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: var(--space-2);
  max-width: min(420px, calc(100vw - 2 * var(--space-4)));
  pointer-events: none;
}

.toast-host__region {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: var(--space-2);
}

.toast {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--color-accent);
  background: var(--color-bg);
  box-shadow: var(--shadow-md);
  color: var(--color-accent-900);
  font-size: 12.5px;
  pointer-events: auto;
}

.toast--info {
  background: color-mix(in srgb, var(--color-accent) 10%, var(--color-bg));
}

.toast--error {
  border-width: 2px;
  border-color: var(--color-accent-800);
  background: var(--color-accent-200);
}

.toast__text {
  min-width: 0;
  overflow-wrap: anywhere;
}

.toast__close {
  margin-left: auto;
  flex: none;
  background: none;
  border: none;
  cursor: pointer;
  font: inherit;
  color: inherit;
  text-decoration: underline;
}
</style>
