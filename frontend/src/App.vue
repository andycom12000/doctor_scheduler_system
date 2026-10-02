<script setup lang="ts">
/**
 * 整個殼：頂部橫向導覽（品牌＋六項連結＋「本機執行 · 免安裝」）＋主區（全寬、內部捲動）。
 * 版面依 docs/design-ref/screen-01.html 的 `.nv` 頂部導覽（issue #52）；
 * 年月切換器不在這裡，改由有 `:ym` 的頁面各自透過 `PageLayout` 顯示在標題列左側。
 */
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ToastHost from '@/components/ToastHost.vue'
import { useYearMonth } from '@/composables/useYearMonth'

const route = useRoute()
const { ym } = useYearMonth()

interface NavItem {
  name: string
  label: string
  to: () => { name: string; params?: Record<string, string> }
}

const navItems: NavItem[] = [
  { name: 'schedule', label: '排班主表', to: () => ({ name: 'schedule', params: { ym: ym.value } }) },
  { name: 'areas', label: '區域與點數', to: () => ({ name: 'areas' }) },
  { name: 'constraints', label: '資格與約束', to: () => ({ name: 'constraints' }) },
  { name: 'variants', label: '變體比較', to: () => ({ name: 'variants', params: { ym: ym.value } }) },
  { name: 'blockedDays', label: '不可排班日登記', to: () => ({ name: 'blockedDays', params: { ym: ym.value } }) },
  { name: 'staff', label: '人員維護', to: () => ({ name: 'staff' }) },
]

const currentName = computed(() => route.name?.toString())
</script>

<template>
  <div class="shell">
    <header class="shell__topbar">
      <div class="shell__brand">
        <span class="shell__brand-mark" aria-hidden="true"></span>
        <span class="shell__brand-name">醫師值班排班系統</span>
      </div>
      <nav class="shell__nav-list" aria-label="主導覽">
        <RouterLink
          v-for="item in navItems"
          :key="item.name"
          :to="item.to()"
          class="shell__nav-item"
          :class="{ 'shell__nav-item--active': currentName === item.name }"
        >
          {{ item.label }}
        </RouterLink>
      </nav>
      <span class="tag tag-neutral shell__badge">本機執行 · 免安裝</span>
    </header>

    <main class="shell__content">
      <RouterView />
    </main>

    <ConfirmDialog />
    <ToastHost />
  </div>
</template>

<style scoped>
.shell {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.shell__topbar {
  display: flex;
  align-items: center;
  gap: var(--space-6);
  height: 48px;
  flex: none;
  padding: 0 var(--space-4);
  border-bottom: 1px solid var(--color-divider);
  background: var(--color-surface);
}

.shell__brand {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: none;
  margin-right: auto;
}

.shell__brand-mark {
  width: 14px;
  height: 14px;
  flex: none;
  border: 1px solid var(--color-accent);
}

.shell__brand-name {
  font-family: var(--font-heading);
  font-weight: var(--font-heading-weight);
  font-size: 15px;
  letter-spacing: 0.04em;
  white-space: nowrap;
}

.shell__nav-list {
  display: flex;
  align-items: center;
  gap: var(--space-6);
  flex: 0 1 auto;
  min-width: 0;
}

.shell__nav-item {
  font-size: 13px;
  color: color-mix(in srgb, var(--color-text) 65%, transparent);
  text-decoration: none;
  white-space: nowrap;
}

.shell__nav-item:hover {
  color: var(--color-text);
}

.shell__nav-item--active {
  color: var(--color-accent);
  font-weight: 500;
}

.shell__badge {
  flex: none;
  margin-left: auto;
}

.shell__content {
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}
</style>
