<script setup lang="ts">
/**
 * 六個畫面共用的頁面骨架：標題列（年月切換器、標題、副標、動作 slot）＋可捲動本體 slot。
 * 畫面 PR 用它包自己的內容，不各自發明頁首（frontend-plan.md §2 第 3 點）。
 *
 * 年月切換器只在路由有 `:ym` 時顯示在標題左側（issue #52）；`/settings/*`、`/staff`
 * 沒有 `:ym`，不顯示。全域導覽已移到 `App.vue` 的頂部橫列，這裡不重複。
 *
 * `fill`（issue #62）：SCREEN 01 V02 月份空狀態要求整頁不捲動，本體改成
 * `overflow:hidden; display:flex`，讓子元素以 `flex:1; min-height:0` 撐滿。連 padding
 * 也一併歸零——空狀態自己算 16px 留白（規格 §3 釘死的數字，跟這裡的 `--space-4` 對不上），
 * 不疊兩層 padding。只有傳 `fill` 的頁面受影響，其餘頁面（`overflow:auto` + 原本 padding）不變。
 */
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import YearMonthSwitcher from '@/components/YearMonthSwitcher.vue'

defineProps<{
  title: string
  subtitle?: string
  fill?: boolean
}>()

const route = useRoute()
const showYearMonth = computed(() => route.params.ym !== undefined)
</script>

<template>
  <div class="page-layout">
    <header class="page-layout__header">
      <YearMonthSwitcher v-if="showYearMonth" class="page-layout__ym" />
      <div class="page-layout__heading">
        <h1 class="page-layout__title">{{ title }}</h1>
        <p v-if="subtitle" class="page-layout__subtitle">{{ subtitle }}</p>
      </div>
      <div class="page-layout__actions">
        <slot name="actions" />
      </div>
    </header>
    <div class="page-layout__body" :class="{ 'page-layout__body--fill': fill }">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.page-layout {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.page-layout__header {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  padding: var(--space-4);
  border-bottom: 1px solid var(--color-divider);
  flex: none;
}

.page-layout__ym {
  flex: none;
}

.page-layout__heading {
  margin-right: auto;
  min-width: 0;
}

.page-layout__title {
  margin: 0;
  font-size: 20px;
}

.page-layout__subtitle {
  margin: 4px 0 0;
  font-size: 12px;
  color: color-mix(in srgb, var(--color-text) 55%, transparent);
}

.page-layout__actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: none;
}

.page-layout__body {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding: var(--space-4);
}

.page-layout__body--fill {
  overflow: hidden;
  display: flex;
  padding: 0;
}
</style>
