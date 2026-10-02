<script setup lang="ts">
/**
 * 全站共用的 toast 容器，掛在 App.vue 裡，固定在畫面底部置中、不擋操作。
 * 案主驗收 #65 時嫌右下角的淡底細框不明顯，改成實心深色底＋白字＋圖示，出現時由下滑入：
 * 一般訊息用 accent-700（與「已發布」badge 同色），錯誤用 neutral-900 加警示圖示，兩者一眼可辨。
 * 一般訊息區是常駐的 `aria-live="polite"`（每則 `role="status"`），錯誤區是常駐的 `role="alert"`。
 * 狀態與計時在 `useToast.ts`。
 */
import { computed } from 'vue'
import { CircleAlert, CircleCheck, X } from 'lucide-vue-next'
import { dismissToast, useToastList } from '@/composables/useToast'

const toasts = useToastList()
const infos = computed(() => toasts.value.filter((t) => t.kind === 'info'))
const errors = computed(() => toasts.value.filter((t) => t.kind === 'error'))
</script>

<template>
  <div class="toast-host">
    <!-- 兩個常駐的 live region，內容在裡面增減（動態才插入的 live region 有些讀屏器不會唸）。 -->
    <TransitionGroup tag="div" name="toast" class="toast-host__region" aria-live="polite">
      <div v-for="toast in infos" :key="toast.id" class="toast toast--info" role="status">
        <CircleCheck class="toast__icon" :size="20" aria-hidden="true" />
        <span class="toast__text">{{ toast.text }}</span>
        <button type="button" class="toast__close" aria-label="關閉訊息" @click="dismissToast(toast.id)">
          <X :size="16" aria-hidden="true" />
        </button>
      </div>
    </TransitionGroup>
    <TransitionGroup tag="div" name="toast" class="toast-host__region" role="alert">
      <div v-for="toast in errors" :key="toast.id" class="toast toast--error">
        <CircleAlert class="toast__icon" :size="20" aria-hidden="true" />
        <span class="toast__text">{{ toast.text }}</span>
        <button type="button" class="toast__close" aria-label="關閉訊息" @click="dismissToast(toast.id)">
          <X :size="16" aria-hidden="true" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
/* 容器本身不吃滑鼠事件，只有每則 toast 吃，這樣不會蓋住底下的按鈕。 */
.toast-host {
  position: fixed;
  left: 50%;
  bottom: var(--space-6, 24px);
  transform: translateX(-50%);
  z-index: 50;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-2);
  width: max-content;
  max-width: min(560px, calc(100vw - 2 * var(--space-4)));
  pointer-events: none;
}

.toast-host__region {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-2);
}

.toast {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-width: 280px;
  padding: var(--space-3) var(--space-3) var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
  color: #fff;
  font-size: 14px;
  font-weight: 500;
  pointer-events: auto;
}

.toast--info {
  background: var(--color-accent-700);
}

.toast--error {
  background: var(--color-neutral-900);
}

.toast__icon {
  flex: none;
}

.toast__text {
  min-width: 0;
  overflow-wrap: anywhere;
}

.toast__close {
  margin-left: auto;
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  background: none;
  border: none;
  border-radius: var(--radius-sm);
  color: inherit;
  opacity: 0.8;
  cursor: pointer;
}

.toast__close:hover {
  opacity: 1;
  background: color-mix(in srgb, #fff 16%, transparent);
}

.toast__close:focus-visible {
  outline: 2px solid #fff;
  outline-offset: 1px;
}

.toast-enter-active,
.toast-leave-active {
  transition:
    transform 0.2s ease,
    opacity 0.2s ease;
}

.toast-enter-from,
.toast-leave-to {
  transform: translateY(12px);
  opacity: 0;
}

@media (prefers-reduced-motion: reduce) {
  .toast-enter-active,
  .toast-leave-active {
    transition: none;
  }
}
</style>
