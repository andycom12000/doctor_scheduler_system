/**
 * 筆刷式矩陣「按住拖過多格連續塗」的共用手法（issue #29；之後 SCREEN 01 的拖拉對調會複製
 * 這個檔案的寫法，語意不同所以不共用同一個 composable，只共用手法）。
 *
 * 手法：`pointerdown` 在起始格 `setPointerCapture`，讓後續的 `pointermove`／`pointerup`
 * 就算游標離開起始格的 DOM 元素，事件仍然持續打在同一個 target 上，不會被半路吃掉；
 * 但我們真正關心的是「游標現在實際懸停在哪一格」，所以 `pointermove` 改用
 * `document.elementFromPoint` 找出游標下的真實元素，再用 `data-*` 屬性回推格子鍵。
 * 同一格在同一次拖曳（同一次 pointerdown → pointerup／pointercancel）只觸發一次 `onPaint`。
 */

/** 讓 `resolvePaintKey` 不需要真的 DOM 就能測——只要求物件有 `closest`，真的 `Element` 天生符合。 */
export interface ClosestLookup {
  closest(selector: string): { getAttribute(name: string): string | null } | null
}

/** 純函式：從一個元素往上找最近帶有 `attribute` 的祖先，讀出格子鍵。找不到回 `null`。 */
export function resolvePaintKey(target: ClosestLookup | null, attribute = 'data-paint-key'): string | null {
  if (!target) return null
  return target.closest(`[${attribute}]`)?.getAttribute(attribute) ?? null
}

export interface StrokeGate {
  /** 這一次拖曳裡這個鍵是不是第一次出現；是的話回 `true` 且記住它，之後同一鍵回 `false`。 */
  visit: (key: string) => boolean
  /** 拖曳結束，清空已塗過的記錄，讓下一次拖曳重新計算。 */
  reset: () => void
}

/** 純函式：一次拖曳內「同一格只塗一次」的去重器。 */
export function createStrokeGate(): StrokeGate {
  let visited = new Set<string>()
  return {
    visit(key) {
      if (visited.has(key)) return false
      visited.add(key)
      return true
    },
    reset() {
      visited = new Set()
    },
  }
}

export interface UsePointerPaintOptions {
  /** 對一格套用目前的筆刷；呼叫端自行判斷登記或清除、以及现況是否已經一致（no-op）。 */
  onPaint: (key: string) => void
  /** 讀取格子鍵的 data 屬性名稱，預設 `data-paint-key`。 */
  attribute?: string
}

export interface PointerPaintHandlers {
  /** 掛在每一格自己的 `@pointerdown`，需要那一格自己的鍵。 */
  onCellPointerDown: (event: PointerEvent, key: string) => void
  /** 掛在整個矩陣容器的 `@pointermove`。 */
  onPointerMove: (event: PointerEvent) => void
  /** 掛在整個矩陣容器的 `@pointerup`。 */
  onPointerUp: (event: PointerEvent) => void
  /** 掛在整個矩陣容器的 `@pointercancel`（例如拖出視窗外、觸控被系統手勢搶走）。 */
  onPointerCancel: (event: PointerEvent) => void
}

export function usePointerPaint(options: UsePointerPaintOptions): PointerPaintHandlers {
  const attribute = options.attribute ?? 'data-paint-key'
  const gate = createStrokeGate()
  let dragging = false

  function paintOnce(key: string | null): void {
    if (!key) return
    if (!gate.visit(key)) return
    options.onPaint(key)
  }

  function onCellPointerDown(event: PointerEvent, key: string): void {
    dragging = true
    gate.reset()
    const target = event.currentTarget as Element | null
    target?.setPointerCapture?.(event.pointerId)
    paintOnce(key)
  }

  function onPointerMove(event: PointerEvent): void {
    if (!dragging) return
    const element = document.elementFromPoint(event.clientX, event.clientY)
    paintOnce(resolvePaintKey(element, attribute))
  }

  function endStroke(): void {
    dragging = false
    gate.reset()
  }

  return {
    onCellPointerDown,
    onPointerMove,
    onPointerUp: endStroke,
    onPointerCancel: endStroke,
  }
}
