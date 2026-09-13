import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { invalidate, resetResourceCache, useResource } from './useResource'

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
