import { beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, ref } from 'vue'
import { invalidate, MAX_CACHE_ENTRIES, resetResourceCache, useResource } from './useResource'

function flushPromises(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0))
}

beforeEach(() => {
  resetResourceCache()
})

describe('useResource', () => {
  it('抓一次資料後 loading／data／error 依序更新', async () => {
    const fetcher = vi.fn().mockResolvedValue({ value: 1 })
    const { data, loading, error } = useResource(ref('a'), fetcher)

    expect(loading.value).toBe(true)
    await flushPromises()

    expect(fetcher).toHaveBeenCalledTimes(1)
    expect(data.value).toEqual({ value: 1 })
    expect(loading.value).toBe(false)
    expect(error.value).toBeNull()
  })

  it('同一個 key 的第二個 consumer 命中快取，不會重打', async () => {
    const fetcher = vi.fn().mockResolvedValue({ value: 1 })
    useResource(ref('shared'), fetcher)
    await flushPromises()

    const second = useResource(ref('shared'), fetcher)
    await flushPromises()

    expect(fetcher).toHaveBeenCalledTimes(1)
    expect(second.data.value).toEqual({ value: 1 })
  })

  it('reload() 無視快取，強制重抓一次', async () => {
    const fetcher = vi.fn().mockResolvedValue({ value: 1 })
    const { reload } = useResource(ref('reload-key'), fetcher)
    await flushPromises()

    await reload()

    expect(fetcher).toHaveBeenCalledTimes(2)
  })

  it('key 换掉各自獨立快取；换回舊 key 不重抓', async () => {
    let call = 0
    const fetcher = vi.fn().mockImplementation(async () => ({ value: ++call }))
    const key = ref('x')
    const { data } = useResource(key, fetcher)
    await flushPromises()
    expect(data.value).toEqual({ value: 1 })

    key.value = 'y'
    await flushPromises()
    expect(data.value).toEqual({ value: 2 })

    key.value = 'x'
    await flushPromises()
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(data.value).toEqual({ value: 1 })
  })

  it('key 為 null 時不發 request', async () => {
    const fetcher = vi.fn()
    useResource(ref<string | null>(null), fetcher)
    await flushPromises()

    expect(fetcher).not.toHaveBeenCalled()
  })

  it('invalidate(prefix) 讓字首相符的 key 失效並立即重抓（前綴以外的 key 不受影響）', async () => {
    const monthFetcher = vi.fn().mockResolvedValue({ status: 'draft' })
    const violationsFetcher = vi.fn().mockResolvedValue({ violations: [] })
    const otherMonthFetcher = vi.fn().mockResolvedValue({ status: 'draft' })

    const month = useResource(ref('schedules/2026-09'), monthFetcher)
    useResource(ref('schedules/2026-09/violations'), violationsFetcher)
    useResource(ref('schedules/2026-10'), otherMonthFetcher)
    await flushPromises()

    monthFetcher.mockResolvedValue({ status: 'published' })
    await invalidate('schedules/2026-09')

    expect(monthFetcher).toHaveBeenCalledTimes(2)
    expect(violationsFetcher).toHaveBeenCalledTimes(2)
    expect(otherMonthFetcher).toHaveBeenCalledTimes(1)
    expect(month.data.value).toEqual({ status: 'published' })
  })

  it('invalidate 不會把切走的舊 key 污染成現在 key 的資料（同一個 consumer 換 key 後 invalidate 再切回舊 key）', async () => {
    // 重現回報的 bug：key=`schedules/${ym}`，09 → 10 → invalidate('schedules') → 回 09
    // 應該拿到 09 自己的資料，不是被 10 的 fetcher 呼叫結果污染。
    const responses: Record<string, { ym: string }> = {
      'schedules/2026-09': { ym: '2026-09' },
      'schedules/2026-10': { ym: '2026-10' },
    }
    const key = ref('schedules/2026-09')
    const fetcher = vi.fn().mockImplementation(async () => responses[key.value])

    const { data } = useResource(key, fetcher)
    await flushPromises()
    expect(data.value).toEqual({ ym: '2026-09' })

    key.value = 'schedules/2026-10'
    await flushPromises()
    expect(data.value).toEqual({ ym: '2026-10' })

    await invalidate('schedules')

    key.value = 'schedules/2026-09'
    await flushPromises()

    expect(data.value).toEqual({ ym: '2026-09' })
  })

  it('invalidate(prefix) 只讓目前還有 consumer 掛著的 key 立即重抓；沒人掛著的 key 只標記待失效、不會被憑空重抓', async () => {
    const monthFetcher = vi.fn().mockResolvedValue({ status: 'draft' })
    const violationsFetcher = vi.fn().mockResolvedValue({ violations: [] })

    const monthKey = ref('schedules/2026-09')
    const violationsKey = ref('schedules/2026-09/violations')
    useResource(monthKey, monthFetcher)
    useResource(violationsKey, violationsFetcher)
    await flushPromises()
    expect(monthFetcher).toHaveBeenCalledTimes(1)
    expect(violationsFetcher).toHaveBeenCalledTimes(1)

    // violations 的 consumer 換去看別的月份，09 的 violations entry 變成沒人掛著；
    // 09 本月的 consumer（monthKey）維持不變，還掛著。
    violationsKey.value = 'schedules/2026-10/violations'
    await flushPromises()
    expect(violationsFetcher).toHaveBeenCalledTimes(2)

    await invalidate('schedules/2026-09')

    // 09 本月還有 consumer 掛著 → 立即重抓
    expect(monthFetcher).toHaveBeenCalledTimes(2)
    // 09 violations 沒人掛著了 → 不會被 invalidate 憑空重抓
    expect(violationsFetcher).toHaveBeenCalledTimes(2)
  })

  it('fetcher 收到 AbortSignal；key 換走時中止還在飛的請求，且不當成載入失敗', async () => {
    const signals: AbortSignal[] = []
    const fetcher = vi.fn().mockImplementation(
      (signal: AbortSignal) =>
        new Promise((_resolve, reject) => {
          signals.push(signal)
          signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
        }),
    )
    const key = ref('abort/a')
    const { error, loading } = useResource(key, fetcher)
    expect(signals).toHaveLength(1)
    expect(signals[0].aborted).toBe(false)

    key.value = 'abort/b'
    await flushPromises()

    expect(signals[0].aborted).toBe(true)
    expect(error.value).toBeNull()
    expect(loading.value).toBe(true) // 新 key 自己的請求
    expect(signals).toHaveLength(2)
  })

  it('同一個 key 的 reload 會中止上一次還沒回來的請求', async () => {
    const signals: AbortSignal[] = []
    const fetcher = vi.fn().mockImplementation((signal: AbortSignal) => {
      signals.push(signal)
      return new Promise(() => {})
    })
    const { reload } = useResource(ref('abort/reload'), fetcher)
    void reload()
    expect(signals).toHaveLength(2)
    expect(signals[0].aborted).toBe(true)
    expect(signals[1].aborted).toBe(false)
  })

  it('被中止的項目回到未載入，下次有人看會重抓', async () => {
    const fetcher = vi
      .fn()
      .mockImplementationOnce(() => new Promise(() => {}))
      .mockResolvedValue({ ok: true })
    const key = ref<string | null>('abort/revisit')
    const { data } = useResource(key, fetcher)
    key.value = null
    await flushPromises()

    const again = useResource(ref('abort/revisit'), fetcher)
    await flushPromises()
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(again.data.value).toEqual({ ok: true })
    expect(data.value).toBeNull()
  })

  it('快取超過上限時淘汰最久沒人看的項目；還有人掛著的不淘汰', async () => {
    const fetcher = vi.fn().mockResolvedValue({ v: 1 })
    const pinned = useResource(ref('evict/pinned'), fetcher)
    await flushPromises()

    for (let i = 0; i < MAX_CACHE_ENTRIES + 10; i++) {
      const scope = effectScope()
      scope.run(() => useResource(ref(`evict/${i}`), fetcher))
      await flushPromises()
      scope.stop() // 離開畫面：沒有 consumer 了
    }
    expect(pinned.data.value).toEqual({ v: 1 })

    const callsBefore = fetcher.mock.calls.length
    // 最早的那幾個已被淘汰 → 再看一次會重抓；最新的還在 → 不重抓
    useResource(ref('evict/0'), fetcher)
    useResource(ref(`evict/${MAX_CACHE_ENTRIES + 9}`), fetcher)
    await flushPromises()
    expect(fetcher.mock.calls.length - callsBefore).toBe(1)
    // 釘住的那個沒被淘汰，再看一次不重抓
    useResource(ref('evict/pinned'), fetcher)
    expect(fetcher.mock.calls.length - callsBefore).toBe(1)
  })

  it('fetcher 失敗時 error 有值、loaded 標記不會卡在 true', async () => {
    const fetcher = vi.fn().mockRejectedValue(new Error('boom'))
    const { data, error, reload } = useResource(ref('failing'), fetcher)
    await flushPromises()

    expect(error.value).toBeInstanceOf(Error)
    expect(data.value).toBeNull()

    // 失敗不會被當成「已快取」，下一輪還是會重抓
    fetcher.mockResolvedValueOnce({ value: 'ok' })
    await reload()
    expect(fetcher).toHaveBeenCalledTimes(2)
  })
})
