<script setup lang="ts">
/**
 * 整個殼：左側導覽（六項＋「本機執行 · 免安裝」）＋頂列年月切換器＋主區（填滿視窗、內部捲動）。
 * 版面依 CLAUDE.md 交付的骨架而非 docs/design-ref（設計稿是上緣一列水平導覽），
 * 見 PR 說明的偏離記錄。
 */
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { CalendarDays, CalendarOff, GitCompare, LayoutGrid, ShieldCheck, Users } from 'lucide-vue-next'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import YearMonthSwitcher from '@/components/YearMonthSwitcher.vue'
import { useYearMonth } from '@/composables/useYearMonth'

const route = useRoute()
const { ym } = useYearMonth()

interface NavItem {
  name: string
  label: string
  icon: typeof CalendarDays
  to: () => { name: string; params?: Record<string, string> }
}

const navItems: NavItem[] = [
  { name: 'schedule', label: '排班主表', icon: CalendarDays, to: () => ({ name: 'schedule', params: { ym: ym.value } }) },
  { name: 'areas', label: '區域與點數', icon: LayoutGrid, to: () => ({ name: 'areas' }) },
  { name: 'constraints', label: '資格與約束', icon: ShieldCheck, to: () => ({ name: 'constraints' }) },
  { name: 'variants', label: '變體比較', icon: GitCompare, to: () => ({ name: 'variants', params: { ym: ym.value } }) },
  { name: 'blockedDays', label: '不可排班日登記', icon: CalendarOff, to: () => ({ name: 'blockedDays', params: { ym: ym.value } }) },
  { name: 'staff', label: '人員維護', icon: Users, to: () => ({ name: 'staff' }) },
]

const currentName = computed(() => route.name?.toString())
</script>

<template>
  <div class="shell">
    <aside class="shell__nav">
      <div class="shell__brand">
        <span class="shell__brand-mark" aria-hidden="true"></span>
        <span class="shell__brand-name">醫師值班排班系統</span>
      </div>
      <nav class="shell__nav-list">
        <RouterLink
          v-for="item in navItems"
          :key="item.name"
          :to="item.to()"
          class="shell__nav-item"
          :class="{ 'shell__nav-item--active': currentName === item.name }"
        >
          <component :is="item.icon" class="shell__nav-icon" :size="16" :stroke-width="1.5" />
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>
      <div class="shell__footer">
        <span class="tag tag-neutral">本機執行 · 免安裝</span>
      </div>
    </aside>

    <div class="shell__main">
      <header class="shell__topbar">
        <YearMonthSwitcher />
      </header>
      <main class="shell__content">
        <RouterView />
      </main>
    </div>

    <ConfirmDialog />
  </div>
</template>

<style scoped>
.shell {
  display: flex;
  height: 100%;
  min-height: 0;
}

.shell__nav {
  display: flex;
  flex-direction: column;
  width: 208px;
  flex: none;
  border-right: 1px solid var(--color-divider);
  background: var(--color-surface);
}

.shell__brand {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-4) var(--space-3);
  border-bottom: 1px solid var(--color-divider);
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
}

.shell__nav-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: var(--space-3) var(--space-2);
  flex: 1;
  overflow-y: auto;
}

.shell__nav-item {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-2) var(--space-3);
  font-size: 13px;
  color: color-mix(in srgb, var(--color-text) 75%, transparent);
  text-decoration: none;
  border-radius: var(--radius-sm);
}

.shell__nav-item:hover {
  background: color-mix(in srgb, var(--color-text) 6%, transparent);
}

.shell__nav-item--active {
  color: var(--color-accent);
  background: var(--color-accent-100);
  font-weight: 500;
}

.shell__nav-icon {
  flex: none;
}

.shell__footer {
  padding: var(--space-3);
  border-top: 1px solid var(--color-divider);
}

.shell__main {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
}

.shell__topbar {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-4);
  border-bottom: 1px solid var(--color-divider);
  flex: none;
}

.shell__content {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}
</style>
