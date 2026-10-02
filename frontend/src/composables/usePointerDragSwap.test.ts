import { describe, expect, it } from 'vitest'
import { createDragSession, DRAG_THRESHOLD_PX } from './usePointerDragSwap'

describe('createDragSession', () => {
  it('位移沒超過門檻就放開：算點擊，不是拖', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 100, 100)
    expect(session.move(102, 101, 'b|2026-09-02')).toBeNull()
    expect(session.dragging).toBe(false)
    expect(session.end(102, 101, 'b|2026-09-02')).toEqual({ kind: 'click' })
  })

  it('超過門檻才開始拖，並標出游標下的目標', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 0, 0)
    expect(session.move(DRAG_THRESHOLD_PX, 0, 'b|2026-09-02')).toEqual({
      sourceKey: 'a|2026-09-01',
      overKey: 'b|2026-09-02',
    })
    expect(session.dragging).toBe(true)
  })

  it('游標回到來源格或離開格子時，目標清掉', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 0, 0)
    session.move(20, 0, 'b|2026-09-02')
    expect(session.move(0, 0, 'a|2026-09-01')?.overKey).toBeNull()
    expect(session.move(40, 0, null)?.overKey).toBeNull()
  })

  it('拖過門檻後再拖回原位，仍算拖（放開不會變成點擊）', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 0, 0)
    session.move(30, 0, 'b|2026-09-02')
    session.move(0, 0, 'a|2026-09-01')
    expect(session.end(0, 0, 'a|2026-09-01')).toEqual({ kind: 'abort' })
  })

  it('放在有效目標上回 drop，帶來源與目標', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 0, 0)
    session.move(30, 0, 'b|2026-09-02')
    expect(session.end(30, 0, 'b|2026-09-02')).toEqual({
      kind: 'drop',
      sourceKey: 'a|2026-09-01',
      targetKey: 'b|2026-09-02',
    })
    expect(session.state).toBeNull()
  })

  it('放在格子外回 abort', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 0, 0)
    session.move(30, 0, 'b|2026-09-02')
    expect(session.end(30, 0, null)).toEqual({ kind: 'abort' })
  })

  it('Esc 取消之後的 pointerup 什麼都不做', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 0, 0)
    session.move(30, 0, 'b|2026-09-02')
    session.cancel()
    expect(session.state).toBeNull()
    expect(session.dragging).toBe(false)
    expect(session.end(30, 0, 'b|2026-09-02')).toEqual({ kind: 'none' })
  })

  it('沒起手就 move／end 都是 no-op', () => {
    const session = createDragSession()
    expect(session.move(50, 50, 'b|2026-09-02')).toBeNull()
    expect(session.end(50, 50, 'b|2026-09-02')).toEqual({ kind: 'none' })
  })

  it('下一次起手不帶上一次的狀態', () => {
    const session = createDragSession()
    session.start('a|2026-09-01', 0, 0)
    session.move(30, 0, 'b|2026-09-02')
    session.end(30, 0, 'b|2026-09-02')
    session.start('c|2026-09-03', 0, 0)
    expect(session.dragging).toBe(false)
    expect(session.end(0, 0, null)).toEqual({ kind: 'click' })
  })
})
