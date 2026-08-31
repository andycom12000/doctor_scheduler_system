<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getHealth } from './api/health'
import { isWebView2Host } from './realtime'

const backend = ref('檢查中…')
const host = isWebView2Host() ? 'WebView2（正式版殼）' : '瀏覽器（開發期）'

onMounted(async () => {
  try {
    const health = await getHealth()
    backend.value = health.status
  } catch (error) {
    backend.value = `連線失敗：${error instanceof Error ? error.message : String(error)}`
  }
})
</script>

<template>
  <main class="shell">
    <h1>醫院排班系統</h1>
    <p class="lede">
      前端工作區骨架。此畫面僅用於驗證環境串接，實作開始後請整個替換掉。
    </p>

    <dl class="status">
      <dt>執行環境</dt>
      <dd>{{ host }}</dd>
      <dt>後端 /api/health</dt>
      <dd>{{ backend }}</dd>
    </dl>

    <p class="hint">
      後端連線失敗屬正常：請確認已啟動 <code>dotnet run --project src/Scheduler.Api</code>，
      或改以 <code>npm run dev:mock</code> 走 MSW。
    </p>
  </main>
</template>

<style scoped>
.shell {
  padding: 2.5rem;
  max-width: 46rem;
}

h1 {
  margin: 0 0 0.5rem;
  font-size: 1.5rem;
}

.lede {
  margin: 0 0 2rem;
  color: #57606a;
}

.status {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 0.5rem 1.5rem;
  margin: 0 0 2rem;
  padding: 1rem 1.25rem;
  background: #fff;
  border: 1px solid #d8dee4;
  border-radius: 6px;
}

.status dt {
  color: #57606a;
}

.status dd {
  margin: 0;
  font-variant-numeric: tabular-nums;
}

.hint {
  color: #57606a;
}

code {
  padding: 0.1em 0.35em;
  background: #eaeef2;
  border-radius: 3px;
  font-size: 0.9em;
}
</style>
