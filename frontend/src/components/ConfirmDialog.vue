<script setup lang="ts">
/**
 * 全站共用一顆，掛在 App.vue 裡。用原生 `<dialog>`（`showModal`/`close`），
 * 呼叫端一律透過 `useConfirm().confirm(...)` 拿 `Promise<boolean>`，不直接操作這個元件。
 */
import { ref, watch } from 'vue'
import { settlePendingConfirm, usePendingConfirm } from '@/composables/useConfirm'

const pending = usePendingConfirm()
const dialogEl = ref<HTMLDialogElement | null>(null)

watch(pending, (value) => {
  // 連續兩次 confirm()：useConfirm() 那邊會先幫前一個 pending 回報「取消」再
  // 覆蓋成新的，Vue 的 watch 是同一輪 microtask 才 flush，這裡看到的可能是
  // 「舊 pending → 新 pending」而不會經過 null，dialog 其實從頭到尾沒關過，
  // 對已經 open 的 `<dialog>` 再呼叫一次 showModal() 會丟 InvalidStateError。
  if (value) {
    if (!dialogEl.value?.open) dialogEl.value?.showModal()
  } else if (dialogEl.value?.open) {
    dialogEl.value.close()
  }
})

function onConfirm(): void {
  settlePendingConfirm(true)
}

function onCancel(): void {
  settlePendingConfirm(false)
}
</script>

<template>
  <dialog ref="dialogEl" class="confirm-dialog" @cancel.prevent="onCancel">
    <template v-if="pending">
      <h2 class="confirm-dialog__title">{{ pending.title }}</h2>
      <p class="confirm-dialog__message">{{ pending.message }}</p>
      <div class="confirm-dialog__actions">
        <button type="button" class="btn btn-secondary" @click="onCancel">
          {{ pending.cancelText ?? '取消' }}
        </button>
        <button type="button" class="btn btn-primary" @click="onConfirm">
          {{ pending.confirmText ?? '確認' }}
        </button>
      </div>
    </template>
  </dialog>
</template>

<style scoped>
.confirm-dialog {
  width: min(420px, calc(100vw - var(--space-8)));
  padding: var(--space-4);
  border: 1px solid var(--color-divider);
  border-radius: var(--radius-lg);
  background: var(--color-surface);
  box-shadow: var(--shadow-lg);
}

.confirm-dialog::backdrop {
  background: color-mix(in srgb, var(--color-neutral-900) 50%, transparent);
}

.confirm-dialog__title {
  margin: 0 0 var(--space-2);
  font-family: var(--font-heading);
  font-weight: var(--font-heading-weight);
  font-size: 20px;
}

.confirm-dialog__message {
  margin: 0;
  font-size: 14px;
  opacity: 0.85;
}

.confirm-dialog__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-4);
}
</style>
