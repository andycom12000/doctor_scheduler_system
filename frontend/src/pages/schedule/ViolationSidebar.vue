<script setup lang="ts">
/**
 * 違規側欄：代碼、嚴重度、誰、哪格。點一筆用 `cellNav.ts` 決定跳去哪個分頁、
 * 捲到哪個格子；由 index.vue 負責切分頁與 `scrollIntoView`，這裡只負責顯示與轉發。
 *
 * 「API 只帶 code 與 severity；底色／斜紋／外框由前端決定。公平性是身分組層級的
 * 分數、指不到格子，不出現在這裡」——這裡列出的都是能指到 cellKeys 的違規。
 */
import type { Violation } from '@/api/types'

defineProps<{ violations: Violation[] }>()

const emit = defineEmits<{ jump: [violation: Violation] }>()
</script>

<template>
  <section class="violation-sidebar">
    <div class="violation-sidebar__title">
      <span class="k">違規</span>
      <span class="tag" :class="violations.some((v) => v.severity === 'hard') ? 'tag-accent' : 'tag-outline'">
        {{ violations.filter((v) => v.severity === 'hard').length ? `${violations.filter((v) => v.severity === 'hard').length} 硬違規` : '無硬違規' }}
      </span>
    </div>
    <div class="violation-sidebar__list">
      <button
        v-for="violation in violations"
        :key="violation.id"
        type="button"
        class="violation-sidebar__row"
        @click="emit('jump', violation)"
      >
        <span class="violation-sidebar__badge" :class="`violation-sidebar__badge--${violation.severity}`">
          {{ violation.severity === 'hard' ? '硬' : '軟' }}
        </span>
        <div class="violation-sidebar__body">
          <div class="violation-sidebar__message">{{ violation.message }}</div>
          <div class="violation-sidebar__code">{{ violation.code }}</div>
        </div>
      </button>
      <p v-if="violations.length === 0" class="violation-sidebar__empty">目前沒有違規。</p>
    </div>
    <p class="violation-sidebar__note">
      API 只帶 code 與 severity；底色／斜紋／外框由前端決定。公平性是身分組層級的分數，指不到格子，不出現在這裡。
    </p>
  </section>
</template>

<style scoped>
.violation-sidebar {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  min-height: 0;
}

.violation-sidebar__title {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.violation-sidebar__title .tag-outline {
  background: transparent;
  border: 1px solid color-mix(in srgb, var(--color-text) 20%, transparent);
  color: color-mix(in srgb, var(--color-text) 60%, transparent);
}

.violation-sidebar__list {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  overflow-y: auto;
}

.violation-sidebar__row {
  display: flex;
  gap: 7px;
  align-items: flex-start;
  padding: 7px 8px;
  border: 1px solid var(--color-divider);
  background: var(--color-bg);
  cursor: pointer;
  text-align: left;
  font: inherit;
  color: inherit;
}

.violation-sidebar__row:hover {
  background: color-mix(in srgb, var(--color-text) 5%, transparent);
}

.violation-sidebar__badge {
  flex: none;
  font: 600 9px ui-monospace, Menlo, monospace;
  padding: 3px 5px;
}

.violation-sidebar__badge--hard {
  background: var(--color-accent);
  color: var(--color-bg);
}

.violation-sidebar__badge--soft {
  background: var(--color-accent-200);
  color: var(--color-accent-900);
}

.violation-sidebar__body {
  min-width: 0;
}

.violation-sidebar__message {
  font-size: 11.5px;
  line-height: 1.35;
}

.violation-sidebar__code {
  font: 600 9px ui-monospace, Menlo, monospace;
  color: color-mix(in srgb, var(--color-text) 46%, transparent);
}

.violation-sidebar__empty {
  margin: 0;
  font-size: 12px;
  color: color-mix(in srgb, var(--color-text) 50%, transparent);
}

.violation-sidebar__note {
  font-size: 10.5px;
  line-height: 1.5;
  color: color-mix(in srgb, var(--color-text) 52%, transparent);
  margin: 7px 0 0;
}
</style>
