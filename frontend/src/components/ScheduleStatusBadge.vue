<script setup lang="ts">
/**
 * 值班表狀態 badge（沒有版本號時只顯示「已發布」）：草稿與已發布兩種顏色語意，只放在年月切換器旁（#65：標題旁不重複放）。
 * 草稿是中性灰底＋虛線框（還沒定版）；已發布是 accent 實底＋白字（已定版），文字帶定版版本號
 * `v{publishedVersion}`。色票全部來自 `styles.css` 的 token。
 */
import { computed } from 'vue'
import { CircleAlert, CircleCheck, PencilLine } from 'lucide-vue-next'
import { scheduleStatusLabel } from './scheduleStatusLabel'

const props = defineProps<{
  status: 'draft' | 'published'
  /** 定版版本號（契約 `publishedVersion`）；只在已發布時顯示。 */
  publishedVersion?: number
  /** 發布後又改過（契約 `editedSincePublish`，#75）。 */
  edited?: boolean
}>()

const published = computed(() => props.status === 'published')
/** 發布後有修改（#75）不能跟「已發布、沒改過」長得一樣：淺底＋虛線框＋警示圖示，提醒還沒定版。 */
const variant = computed(() =>
  !published.value ? 'status-badge--draft' : props.edited ? 'status-badge--edited' : 'status-badge--published',
)
const icon = computed(() => (!published.value ? PencilLine : props.edited ? CircleAlert : CircleCheck))
const label = computed(() => scheduleStatusLabel(props.status, props.publishedVersion, props.edited ?? false))
</script>

<template>
  <span class="status-badge" :class="variant" :title="edited && published ? '發布後又改過，還沒重新發布' : undefined">
    <component :is="icon" class="status-badge__icon" :size="13" aria-hidden="true" />
    {{ label }}
  </span>
</template>

<style scoped>
.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  flex: none;
  padding: 3px 10px;
  border: 1px solid transparent;
  border-radius: calc(var(--radius-md) * 0.75);
  font: 600 12px/1.2 var(--font-body);
  letter-spacing: 0.02em;
  white-space: nowrap;
}

.status-badge__icon {
  flex: none;
}

.status-badge--draft {
  background: var(--color-neutral-200);
  border-color: var(--color-neutral-600);
  border-style: dashed;
  color: var(--color-neutral-900);
}

.status-badge--published {
  background: var(--color-accent-700);
  border-color: var(--color-accent-700);
  color: var(--color-accent-100);
}

.status-badge--edited {
  background: var(--color-accent-100);
  border-color: var(--color-accent-700);
  border-style: dashed;
  color: var(--color-accent-900);
}
</style>
