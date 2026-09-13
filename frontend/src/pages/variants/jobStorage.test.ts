import { describe, expect, it } from 'vitest'
import { lastJobStorageKey, readLastJobId, rememberJobId, resolveJobId, type KeyValueStore } from './jobStorage'

function fakeStorage(initial: Record<string, string> = {}): KeyValueStore {
  const map = new Map(Object.entries(initial))
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => {
      map.set(key, value)
    },
  }
}

describe('lastJobStorageKey', () => {
  it('鍵含年月', () => {
    expect(lastJobStorageKey('2026-09')).toBe('solver:lastJob:2026-09')
  })
})

describe('readLastJobId / rememberJobId', () => {
  it('寫入後可讀回同一個月的 jobId', () => {
    const storage = fakeStorage()
    rememberJobId('2026-09', 'job-42', storage)
    expect(readLastJobId('2026-09', storage)).toBe('job-42')
  })

  it('沒有記錄過的月份回 null', () => {
    expect(readLastJobId('2026-10', fakeStorage())).toBeNull()
  })

  it('storage 為 null（環境不支援）時安全地回 null、不丟例外', () => {
    expect(readLastJobId('2026-09', null)).toBeNull()
    expect(() => rememberJobId('2026-09', 'job-1', null)).not.toThrow()
  })

  it('storage 讀寫丟例外時吞下、不往外丟', () => {
    const throwing: KeyValueStore = {
      getItem: () => {
        throw new Error('blocked')
      },
      setItem: () => {
        throw new Error('blocked')
      },
    }
    expect(readLastJobId('2026-09', throwing)).toBeNull()
    expect(() => rememberJobId('2026-09', 'job-1', throwing)).not.toThrow()
  })
})

describe('resolveJobId', () => {
  it('route query 的 job 優先', () => {
    const storage = fakeStorage({ 'solver:lastJob:2026-09': 'job-old' })
    expect(resolveJobId('job-new', '2026-09', storage)).toBe('job-new')
  })

  it('query 是陣列時取第一個', () => {
    expect(resolveJobId(['job-a', 'job-b'], '2026-09', fakeStorage())).toBe('job-a')
  })

  it('沒有 query 時退回 localStorage', () => {
    const storage = fakeStorage({ 'solver:lastJob:2026-09': 'job-old' })
    expect(resolveJobId(undefined, '2026-09', storage)).toBe('job-old')
  })

  it('兩者都沒有時回 null', () => {
    expect(resolveJobId(undefined, '2026-09', fakeStorage())).toBeNull()
  })

  it('query 是 null（vue-router 沒帶這個參數時的實際型別）時退回 localStorage', () => {
    const storage = fakeStorage({ 'solver:lastJob:2026-09': 'job-old' })
    expect(resolveJobId(null, '2026-09', storage)).toBe('job-old')
  })
})
