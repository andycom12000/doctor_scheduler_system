import { describe, expect, it, vi } from 'vitest'
import { createStrokeGate, resolvePaintKey, usePointerPaint } from './usePointerPaint'

describe('resolvePaintKey', () => {
  it('從最近的帶有 data 屬性的祖先讀出格子鍵', () => {
    const cell = { getAttribute: (name: string) => (name === 'data-paint-key' ? 'staff-001|2026-09-05' : null) }
    const target = { closest: () => cell }
    expect(resolvePaintKey(target)).toBe('staff-001|2026-09-05')
  })

  it('找不到帶屬性的祖先回 null', () => {
    const target = { closest: () => null }
    expect(resolvePaintKey(target)).toBeNull()
  })

  it('target 本身是 null 回 null', () => {
    expect(resolvePaintKey(null)).toBeNull()
  })

  it('可自訂屬性名稱', () => {
    const cell = { getAttribute: (name: string) => (name === 'data-cell' ? 'x' : null) }
    const target = { closest: (selector: string) => (selector === '[data-cell]' ? cell : null) }
    expect(resolvePaintKey(target, 'data-cell')).toBe('x')
  })
})

describe('createStrokeGate', () => {
  it('同一鍵在同一次拖曳只回傳一次 true', () => {
    const gate = createStrokeGate()
    expect(gate.visit('a')).toBe(true)
    expect(gate.visit('a')).toBe(false)
    expect(gate.visit('b')).toBe(true)
  })

  it('reset 之後同一鍵可以再被塗一次', () => {
    const gate = createStrokeGate()
    gate.visit('a')
    gate.reset()
    expect(gate.visit('a')).toBe(true)
  })
})

/** 用假的 PointerEvent／Element 測試 composable 本身不需要真的 pointermove／document，
 * 只測試「pointerdown 立刻塗一次、pointerup 之後可以再塗同一格」這條不依賴 `document` 的路徑。
 * 預設是主指標的左鍵按下（`button: 0, isPrimary: true`），符合真正會起筆的情境。 */
function fakePointerDownEvent(overrides: Record<string, unknown> = {}): PointerEvent {
  return {
    pointerId: 1,
    button: 0,
    isPrimary: true,
    currentTarget: { setPointerCapture: vi.fn() },
    ...overrides,
  } as unknown as PointerEvent
}

describe('usePointerPaint', () => {
  it('pointerdown 立刻對起始格套用一次筆刷', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown } = usePointerPaint({ onPaint })
    onCellPointerDown(fakePointerDownEvent(), 'staff-001|2026-09-05')
    expect(onPaint).toHaveBeenCalledTimes(1)
    expect(onPaint).toHaveBeenCalledWith('staff-001|2026-09-05')
  })

  it('pointerup 結束這次拖曳後，下一次 pointerdown 對同一格可以再塗一次', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown, onPointerUp } = usePointerPaint({ onPaint })
    onCellPointerDown(fakePointerDownEvent(), 'k')
    onPointerUp({} as PointerEvent)
    onCellPointerDown(fakePointerDownEvent(), 'k')
    expect(onPaint).toHaveBeenCalledTimes(2)
  })

  it('setPointerCapture 會用 pointerId 呼叫在 currentTarget 上', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown } = usePointerPaint({ onPaint })
    const capture = vi.fn()
    onCellPointerDown(fakePointerDownEvent({ pointerId: 7, currentTarget: { setPointerCapture: capture } }), 'k')
    expect(capture).toHaveBeenCalledWith(7)
  })

  it('非左鍵（例如右鍵拖曳）不起筆，也不佔用之後的 pointermove', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown } = usePointerPaint({ onPaint })
    onCellPointerDown(fakePointerDownEvent({ button: 2 }), 'k')
    expect(onPaint).not.toHaveBeenCalled()
  })

  it('非主指標（多點觸控的第二指）不起筆', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown } = usePointerPaint({ onPaint })
    onCellPointerDown(fakePointerDownEvent({ isPrimary: false }), 'k')
    expect(onPaint).not.toHaveBeenCalled()
  })

  it('onPointerUp 回傳這一筆畫是否塗過格子：有起筆為 true', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown, onPointerUp } = usePointerPaint({ onPaint })
    onCellPointerDown(fakePointerDownEvent(), 'k')
    expect(onPointerUp({} as PointerEvent)).toBe(true)
  })

  it('onPointerUp 回傳這一筆畫是否塗過格子：被過濾掉的 pointerdown 不算塗過，回 false', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown, onPointerUp } = usePointerPaint({ onPaint })
    onCellPointerDown(fakePointerDownEvent({ button: 2 }), 'k')
    expect(onPointerUp({} as PointerEvent)).toBe(false)
  })

  it('onPointerCancel 回傳值意義與 onPointerUp 相同', () => {
    const onPaint = vi.fn()
    const { onCellPointerDown, onPointerCancel } = usePointerPaint({ onPaint })
    onCellPointerDown(fakePointerDownEvent(), 'k')
    expect(onPointerCancel({} as PointerEvent)).toBe(true)
  })
})
