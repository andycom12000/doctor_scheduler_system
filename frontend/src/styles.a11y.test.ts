import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const read = (rel: string) => readFileSync(fileURLToPath(new URL(rel, import.meta.url)), 'utf8')

// 色票從 styles.css 讀，不手抄。目前 :root 只有亮色 token，沒有暗色主題。
const css = read('./styles.css')
const token = (name: string) => {
  const m = new RegExp(`${name}:\\s*(#[0-9a-fA-F]{6})`).exec(css)
  if (!m) throw new Error(`styles.css 找不到 ${name}`)
  return m[1]
}
const bg = token('--color-bg')
const text = token('--color-text')

function lum(hex: string): number {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const f = (v: number) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)
  return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2])
}
function blendedContrast(pct: number): number {
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
    'NoStaffGuide.vue': ['./components/NoStaffGuide.vue', '.no-staff__kicker'],
    'EmptyState.vue': ['./pages/schedule/EmptyState.vue', '.k'],
  } as const
  for (const [name, [rel, selector]] of Object.entries(files)) {
    it(`${name} 的 ${selector}`, () => {
      const src = read(rel)
      const block = src.slice(src.indexOf(`${selector} {`))
      const m = /color-mix\(in srgb, var\(--color-text\) (\d+)%, transparent\)/.exec(block.slice(0, block.indexOf('}')))
      expect(m).not.toBeNull()
      expect(blendedContrast(Number(m![1]) / 100)).toBeGreaterThanOrEqual(4.5)
    })
  }
})
