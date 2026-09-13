<script setup lang="ts">
import PageLayout from '@/components/PageLayout.vue'
import { useYearMonth } from '@/composables/useYearMonth'

const { ym } = useYearMonth()
</script>

<template>
  <PageLayout title="不可排班日登記" :subtitle="`年月：${ym}`">
    <p>尚未實作，見 issue #29。</p>
  </PageLayout>
</template>
