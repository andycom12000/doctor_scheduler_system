<script setup lang="ts">
/**
 * 發布前的硬違規提示（#68）：工具列「發布」旁的深色警示標籤，視覺延續 ToastHost 的錯誤樣式
 * （neutral-900 實底、白字、警示圖示）。點擊由 index.vue 捲到右側違規清單。
 * 沒有硬違規時由呼叫端不渲染。
 */
import { TriangleAlert } from 'lucide-vue-next'
import type { HardViolationBadgeInfo } from './lib/hardViolationBadge'

defineProps<{ info: HardViolationBadgeInfo }>()
defineEmits<{ click: [] }>()
</script>

<template>
  <button type="button" class="hard-badge" :aria-label="`${info.label}，點擊查看違規清單`" @click="$emit('click')">
    <TriangleAlert class="hard-badge__icon" :size="18" aria-hidden="true" />
    <span class="hard-badge__text">
      <span class="hard-badge__headline">{{ info.headline }}</span>
      <span v-if="info.detail" class="hard-badge__detail">{{ info.detail }}</span>
    </span>
  </button>
</template>

<style scoped>
.hard-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  padding: 4px var(--space-3);
  border: none;
  border-radius: var(--radius-md);
  background: var(--color-neutral-900);
  color: #fff;
  text-align: left;
  cursor: pointer;
}

.hard-badge:hover {
  opacity: 0.9;
}

.hard-badge:focus-visible {
  outline: 2px solid var(--color-accent);
  outline-offset: 2px;
}

.hard-badge__icon {
  flex: none;
}

.hard-badge__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.hard-badge__headline {
  font-size: 13px;
  font-weight: 600;
  line-height: 1.3;
}

.hard-badge__detail {
  font-size: 11px;
  font-weight: 500;
  line-height: 1.3;
  opacity: 0.85;
}
</style>
