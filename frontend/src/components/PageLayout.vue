<script setup lang="ts">
/**
 * 六個畫面共用的頁面骨架：標題列（標題、副標、動作 slot）＋可捲動本體 slot。
 * 畫面 PR 用它包自己的內容，不各自發明頁首（frontend-plan.md §2 第 3 點）。
 */
defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <div class="page-layout">
    <header class="page-layout__header">
      <div class="page-layout__heading">
        <h1 class="page-layout__title">{{ title }}</h1>
        <p v-if="subtitle" class="page-layout__subtitle">{{ subtitle }}</p>
      </div>
      <div class="page-layout__actions">
        <slot name="actions" />
      </div>
    </header>
    <div class="page-layout__body">
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
  align-items: flex-start;
  gap: var(--space-4);
  padding: var(--space-4);
  border-bottom: 1px solid var(--color-divider);
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
</style>
