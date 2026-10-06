<script setup lang="ts">
/**
 * 名冊沒有任何在職人員時，取代「開始求解」入口的引導（issue #81）：
 * 排班主表、變體比較、不可排班日登記三頁共用。
 *
 * 一句話說明 + 一個前往「人員維護」的按鈕，不放求解按鈕。自己置中、不依賴格線撐版面，
 * 所以在 `fill` 與一般捲動容器裡都不會被壓縮。語氣與框線沿用 SCREEN 01 V02 空狀態
 * （docs/design-ref/screen-01-v02-empty-state.md §5.2）。
 */
import { RouterLink } from 'vue-router'

defineProps<{
  /** 名冊有人但全部停用：文案改成請去啟用。 */
  allInactive?: boolean
}>()
</script>

<template>
  <div class="no-staff">
    <div class="no-staff__panel blueprint" role="region" aria-labelledby="no-staff-title" data-testid="no-staff-guide">
      <i class="corner tl" aria-hidden="true"></i>
      <i class="corner tr" aria-hidden="true"></i>
      <i class="corner bl" aria-hidden="true"></i>
      <i class="corner br" aria-hidden="true"></i>
      <span class="no-staff__kicker">NO ACTIVE STAFF</span>
      <div id="no-staff-title" class="no-staff__title">{{ allInactive ? '沒有在職人員' : '尚未建立人員' }}</div>
      <p class="no-staff__hint">
        {{ allInactive ? '名冊裡的人員都已停用，無法求解。請到「人員維護」啟用人員。' : '名冊裡沒有任何在職人員，無法求解。請先到「人員維護」新增人員。' }}
      </p>
      <RouterLink class="btn btn-primary no-staff__cta" :to="{ name: 'staff' }">前往人員維護</RouterLink>
    </div>
  </div>
</template>

<style scoped>
.no-staff {
  flex: 1;
  min-width: 0;
  min-height: 320px;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
  box-sizing: border-box;
}

.no-staff__panel {
  position: relative;
  max-width: 100%;
  box-sizing: border-box;
  background: var(--color-bg);
  border: 1px solid var(--color-accent);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 11px;
  text-align: center;
  padding: 32px 48px;
}

.no-staff__kicker {
  font: 600 10px/1 var(--font-heading);
  letter-spacing: 0.12em;
  color: color-mix(in srgb, var(--color-text) 70%, transparent);
}

.no-staff__title {
  font: 600 34px/1 var(--font-heading);
  white-space: nowrap;
}

.no-staff__hint {
  margin: 0;
  font-size: 13px;
  color: var(--color-text);
}

.no-staff__cta {
  margin-top: 4px;
  padding: 10px 22px;
  font: 600 13.5px/1 var(--font-heading);
  letter-spacing: 0.05em;
  text-decoration: none;
}

/* `.blueprint` 四角記號，與 schedule/EmptyState.vue 同一份寫法（那份是 scoped，無法共用）。 */
.blueprint > .corner {
  position: absolute;
  width: 11px;
  height: 11px;
  color: var(--color-accent);
}

.blueprint > .corner::before,
.blueprint > .corner::after {
  content: '';
  position: absolute;
  background: currentColor;
}

.blueprint > .corner::before {
  left: 5px;
  top: 0;
  width: 1px;
  height: 100%;
}

.blueprint > .corner::after {
  top: 5px;
  left: 0;
  width: 100%;
  height: 1px;
}

.blueprint > .corner.tl {
  top: -6px;
  left: -6px;
}

.blueprint > .corner.tr {
  top: -6px;
  right: -6px;
}

.blueprint > .corner.bl {
  bottom: -6px;
  left: -6px;
}

.blueprint > .corner.br {
  bottom: -6px;
  right: -6px;
}
</style>
