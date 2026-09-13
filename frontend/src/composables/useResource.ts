/**
 * 自寫的資料快取，取代 vue-query（README／frontend-plan.md §1：寫入回應都帶新的 revision，
 * 失效規則是「同一 ym 的所有讀取」一條而已，不需要重試／背景更新／視窗聚焦重抓）。
 *
 * key 慣例：清單類 `schedules`、單月 `schedules/2026-09`、子資源
 * `schedules/2026-09/violations`。`invalidate(prefix)` 用字首比對，
 * 寫入命令成功後呼叫 `invalidate('schedules/2026-09')` 會讓該月所有子資源一起失效。
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'

interface CacheEntry<T> {
  data: Ref<T | null>
  error: Ref<unknown>
  loading: Ref<boolean>
  /** 目前的值是否已經成功抓過一次；invalidate 會把它撥回 false。 */
  loaded: boolean
  /** 忽略過期（out-of-order）回應用。 */
  requestId: number
  /**
   * 目前正盯著這個 key 的 consumer 各自登記的重抓 callback。
   *
   * 不能只存「最後一個 fetcher」——`fetcher` 通常是像
   * `() => getSchedule(ym.value)` 這種讀外部 ref 目前值的閉包，key 換掉時
   * （例如 09 月切到 10 月）舊 entry 跟新 entry 會拿到「同一個」閉包，之後
   * invalidate 若直接呼叫 entry 存的 fetcher，讀到的永遠是外部 ref**現在**的
   * 值，會把舊 entry 的資料污染成現在 key 的資料。改成每個 consumer 在
   * `watch(keyRef, ...)` 命中某個 key 時，把「重抓自己」的 callback 登記到
   * *那個 key 對應的 entry*，key 換走時（`onCleanup`）立刻從舊 entry 除籍。
   * 這樣 invalidate 呼叫某個 entry 裡的 callback 時，登記當下的 key 保證還是
   * 呼叫當下 consumer 正在看的 key，不會抓錯。沒有任何 callback 的 entry
   * （沒人掛著）只標記 `loaded = false`，不會被憑空重抓。
   */
  watchers: Set<() => Promise<void>>
}

const cache = new Map<string, CacheEntry<unknown>>()

function getEntry<T>(key: string): CacheEntry<T> {
  let entry = cache.get(key) as CacheEntry<T> | undefined
  if (!entry) {
    entry = {
      data: ref(null) as Ref<T | null>,
      error: ref(null),
      loading: ref(false),
      loaded: false,
      requestId: 0,
      watchers: new Set(),
    }
    cache.set(key, entry as CacheEntry<unknown>)
  }
  return entry
}

async function fetchInto<T>(key: string, fetcher: () => Promise<T>): Promise<void> {
  const entry = getEntry<T>(key)
  const requestId = ++entry.requestId
  entry.loading.value = true
  entry.error.value = null
  try {
    const result = await fetcher()
    if (entry.requestId === requestId) {
      entry.data.value = result
      entry.loaded = true
    }
  } catch (err) {
    if (entry.requestId === requestId) {
      entry.error.value = err
      entry.loaded = false
    }
  } finally {
    if (entry.requestId === requestId) {
      entry.loading.value = false
    }
  }
}

export interface UseResourceResult<T> {
  data: ComputedRef<T | null>
  error: ComputedRef<unknown>
  loading: ComputedRef<boolean>
  /** 強制重抓，不看快取是否已有資料。 */
  reload: () => Promise<void>
}

/**
 * `keyRef` 為 `null`／`undefined` 時視為「還沒準備好」，不會發request（例如年月還沒從路由讀出來）。
 * 同一個 key 若已經抓過且未被 `invalidate`，換頁再切回來不會重打一次。
 */
export function useResource<T>(
  keyRef: Ref<string | null | undefined>,
  fetcher: () => Promise<T>,
): UseResourceResult<T> {
  watch(
    keyRef,
    (key, _oldKey, onCleanup) => {
      if (!key) return
      const entry = getEntry<T>(key)
      const refetch = () => fetchInto(key, fetcher)
      entry.watchers.add(refetch)
      onCleanup(() => entry.watchers.delete(refetch))
      if (!entry.loaded && !entry.loading.value) {
        void refetch()
      }
    },
    { immediate: true },
  )

  const reload = async () => {
    const key = keyRef.value
    if (!key) return
    await fetchInto(key, fetcher)
  }

  return {
    data: computed(() => (keyRef.value ? getEntry<T>(keyRef.value).data.value : null)),
    error: computed(() => (keyRef.value ? getEntry<T>(keyRef.value).error.value : null)),
    loading: computed(() => (keyRef.value ? getEntry<T>(keyRef.value).loading.value : false)),
    reload,
  }
}

/**
 * 讓所有 `key === prefix` 或 `key` 以 `${prefix}/` 開頭的快取項目失效。
 *
 * 只有「目前還有 consumer 掛著」的 key 會立即重抓（用該 consumer 自己登記的
 * callback，保證抓的是它現在真的在看的 key）；沒人掛著的 key 只標記
 * `loaded = false`，下次有人 `useResource` 它時會自然重抓，不會在這裡憑空
 * 把所有曾經快取過的月份重抓一次。
 */
export async function invalidate(prefix: string): Promise<void> {
  const matched = [...cache.entries()].filter(([key]) => key === prefix || key.startsWith(`${prefix}/`))
  await Promise.all(
    matched.map(async ([, entry]) => {
      entry.loaded = false
      if (entry.watchers.size === 0) return
      await Promise.all([...entry.watchers].map((refetch) => refetch()))
    }),
  )
}

/** 測試專用：清空模組層級的快取，避免測試之間互相汙染。 */
export function resetResourceCache(): void {
  cache.clear()
}
