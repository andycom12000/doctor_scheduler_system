<script setup lang="ts">
import PageLayout from '@/components/PageLayout.vue'
import { useYearMonth } from '@/composables/useYearMonth'

const { ym } = useYearMonth()
</script>

<template>
  <PageLayout title="排班主表" :subtitle="`年月：${ym}`">
    <p>尚未實作，見 issue #31。</p>
  </PageLayout>
</template>
