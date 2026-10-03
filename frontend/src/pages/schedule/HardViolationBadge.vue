<script setup lang="ts">
/**
 * 硬違規摘要卡（#68）：違規側欄最上方的深色區塊，視覺延續 ToastHost 的錯誤樣式
 * （neutral-900 實底、白字、警示圖示）。原本放在工具列「發布」旁，案主驗收時嫌把工具列擠滿，
 * 改放到違規清單頂端，跟明細在一起。沒有硬違規時由呼叫端不渲染。
 */
import { TriangleAlert } from 'lucide-vue-next'
import type { HardViolationBadgeInfo } from './lib/hardViolationBadge'

defineProps<{ info: HardViolationBadgeInfo }>()
</script>

<template>
  <div class="hard-summary" role="status" :aria-label="info.label">
    <TriangleAlert class="hard-summary__icon" :size="20" aria-hidden="true" />
    <div class="hard-summary__text">
      <span class="hard-summary__headline">{{ info.headline }}</span>
      <span v-if="info.detail" class="hard-summary__detail">{{ info.detail }}</span>
    </div>
  </div>
</template>

<style scoped>
.hard-summary {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  padding: var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-neutral-900);
  color: #fff;
}

.hard-summary__icon {
  flex: none;
  margin-top: 1px;
}

.hard-summary__text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.hard-summary__headline {
  font-size: 15px;
  font-weight: 600;
  line-height: 1.3;
}

.hard-summary__detail {
  font-size: 12.5px;
  font-weight: 500;
  line-height: 1.45;
  opacity: 0.9;
}
</style>
