import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import type { ListStaffResponse } from '@/api/types'
import {
  NO_ACTIVE_STAFF_REASON,
  STAFF_LOAD_FAILED_REASON,
  STAFF_LOADING_REASON,
  solveDisabledReason,
} from './rosterGuard'

const res = (active: number, inactive = 0) => ({ items: [], counts: { active, inactive } }) as unknown as ListStaffResponse

describe('solveDisabledReason（issue #95）', () => {
  it('有在職人員：可求解，無原因', () => {
    expect(solveDisabledReason(res(3), false)).toBeNull()
  })
  it('沒有在職人員：回名冊原因', () => {
    expect(solveDisabledReason(res(0), false)).toBe(NO_ACTIVE_STAFF_REASON)
    expect(solveDisabledReason(res(0, 2), false)).toBe(NO_ACTIVE_STAFF_REASON)
  })
  it('讀取失敗：回失敗提示，不是沉默的 disabled', () => {
    expect(solveDisabledReason(null, true)).toBe(STAFF_LOAD_FAILED_REASON)
  })
  it('載入中：回載入提示', () => {
    expect(solveDisabledReason(undefined, false)).toBe(STAFF_LOADING_REASON)
  })
})

// kicker 對比：文字色 --color-text 以 N% 疊在 --color-bg（面板底）上，對 --color-bg 算 WCAG 對比。
// 目前只有亮色主題（styles.css 沒有暗色 token）；新增暗色主題時要把它的 token 加進 themes。
const themes = { light: { bg: '#f2f2f3', text: '#1d1f20' } }

function lum(hex: string): number {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const f = (v: number) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)
  return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2])
}
function blendedContrast(bg: string, text: string, pct: number): number {
  const mix = [1, 3, 5].map((i) => {
    const b = parseInt(bg.slice(i, i + 2), 16)
    const t = parseInt(text.slice(i, i + 2), 16)
    return Math.round(t * pct + b * (1 - pct))
  })
  const hex = '#' + mix.map((v) => v.toString(16).padStart(2, '0')).join('')
  const [a, b] = [lum(hex), lum(bg)]
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

describe('空狀態英文 kicker 對比 ≥ 4.5:1（issue #95）', () => {
  const files = {
    'NoStaffGuide.vue': ['../../components/NoStaffGuide.vue', '.no-staff__kicker'],
    'EmptyState.vue': ['../schedule/EmptyState.vue', '.k'],
  } as const
  for (const [name, [rel, selector]] of Object.entries(files)) {
    it(`${name} 的 ${selector}`, () => {
      const src = readFileSync(fileURLToPath(new URL(rel, import.meta.url)), 'utf8')
      const block = src.slice(src.indexOf(`${selector} {`))
      const m = /color-mix\(in srgb, var\(--color-text\) (\d+)%, transparent\)/.exec(block.slice(0, block.indexOf('}')))
      expect(m).not.toBeNull()
      for (const t of Object.values(themes)) {
        expect(blendedContrast(t.bg, t.text, Number(m![1]) / 100)).toBeGreaterThanOrEqual(4.5)
      }
    })
  }
})
