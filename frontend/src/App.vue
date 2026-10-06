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
import CalendarSyncOverlay from '@/components/CalendarSyncOverlay.vue'
import { useYearMonth } from '@/composables/useYearMonth'

const route = useRoute()
const { ym } = useYearMonth()

// 建置時由 vite.config.ts 注入（來源：Directory.Build.props 的 <Version>；dev 伺服器帶 -dev）
const appVersion = __APP_VERSION__

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
    <header class="shell__topbar screen-only">
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
      <div class="shell__badge">
        <span class="tag tag-neutral shell__tag">本機執行 · 免安裝</span>
        <span class="shell__version" title="版本">v{{ appVersion }}</span>
      </div>
    </header>

    <!-- 行事曆自動更新（#112）：同步中全畫面鎖住、失敗只能重試／略過；狀態機在 useCalendarSyncNotice -->
    <CalendarSyncOverlay />

    <main class="shell__content">
      <RouterView />
    </main>

    <ConfirmDialog class="screen-only" />
    <ToastHost class="screen-only" />
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
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: none;
  margin-left: auto;
}

.shell__version {
  font-size: 11px;
  white-space: nowrap;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.shell__content {
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

/* 窄視窗先讓出版本號，導覽不能被擠掉 */
@media (max-width: 900px) {
  .shell__badge {
    display: none;
  }

  .shell__topbar {
    gap: var(--space-4);
  }

  .shell__nav-list {
    gap: var(--space-4);
  }
}

/* 再窄：導覽縮字距，最後保底可橫向捲動（不重疊、仍可點） */
@media (max-width: 720px) {
  .shell__topbar {
    gap: var(--space-3);
    padding: 0 var(--space-3);
  }

  .shell__nav-list {
    gap: var(--space-3);
    overflow-x: auto;
    scrollbar-width: none;
  }

  .shell__brand-name {
    letter-spacing: 0;
  }
}

@media print {
  .shell {
    display: block;
    height: auto;
  }

  .shell__content {
    overflow: visible;
  }
}
</style>
