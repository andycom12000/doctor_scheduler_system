/**
 * 「按住一格拖到另一格、放開對調」的 Pointer Events 手法（issue #34，SCREEN 01 區域 × 日）。
 *
 * 跟 `usePointerPaint` 同一套手法（`pointerdown` 起手 `setPointerCapture`、`pointermove`
 * 用 `document.elementFromPoint` 找游標下真實的格子、`data-*` 屬性回推鍵），但語意不同：
 * 筆刷是「經過的每格都塗」，這裡是「單一來源 → 單一目標」，而且要能取消，所以另寫、
 * 只共用 `resolvePaintKey`。
 *
 * 與「點格開候選人面板」共存：位移沒超過 `threshold` 就不算拖，放開時交給原本的 `click`；
 * 真的拖過（放下或 Esc 取消）之後，緊接著那個 `click` 要吞掉，否則候選人面板會疊在對調結果上——
 * 呼叫端的 `@click` 先問 `consumeClickSuppression()`。
 *
 * 狀態機拆成 `createDragSession`（不碰 DOM，可單獨測），composable 只負責接 DOM 事件與 Esc。
 */
import { onBeforeUnmount, shallowRef, type ShallowRef } from 'vue'
import { resolvePaintKey } from './usePointerPaint'

/** 位移小於這個像素數視為點擊，不算拖（滑鼠手抖、觸控點按的容許範圍）。 */
export const DRAG_THRESHOLD_PX = 5

export interface DragState {
  sourceKey: string
  /** 目前游標下可放的目標；沒有（在來源上、在格子外）為 null。 */
  overKey: string | null
}

export type DragEnd =
  /** 沒有進行中的拖曳（例如已被 Esc 取消後才放開）。 */
  | { kind: 'none' }
  /** 位移沒超過門檻：不是拖，交給 click。 */
  | { kind: 'click' }
  /** 拖過但沒放在有效目標上（放回來源、放在格子外）。 */
  | { kind: 'abort' }
  | { kind: 'drop'; sourceKey: string; targetKey: string }

export interface DragSession {
  start: (key: string, x: number, y: number) => void
  /** `hoverKey` 是游標下的格子鍵（由呼叫端用 elementFromPoint 查）。回傳目前狀態，拖曳未成立為 null。 */
  move: (x: number, y: number, hoverKey: string | null) => DragState | null
  end: (x: number, y: number, hoverKey: string | null) => DragEnd
  cancel: () => void
  readonly state: DragState | null
  /** 位移已超過門檻（Esc 取消前為 true，取消後歸 false）。 */
  readonly dragging: boolean
  /** 有起手但還沒結束（含還沒超過門檻）。 */
  readonly active: boolean
}

export function createDragSession(threshold = DRAG_THRESHOLD_PX): DragSession {
  let sourceKey: string | null = null
  let originX = 0
  let originY = 0
  let dragging = false
  let overKey: string | null = null

  function reset(): void {
    sourceKey = null
    dragging = false
    overKey = null
  }

  function currentState(): DragState | null {
    return sourceKey !== null && dragging ? { sourceKey, overKey } : null
  }

  return {
    start(key, x, y) {
      sourceKey = key
      originX = x
      originY = y
      dragging = false
      overKey = null
    },
    move(x, y, hoverKey) {
      if (sourceKey === null) return null
      if (!dragging && Math.hypot(x - originX, y - originY) >= threshold) dragging = true
      if (dragging) overKey = hoverKey !== null && hoverKey !== sourceKey ? hoverKey : null
      return currentState()
    },
    end(_x, _y, hoverKey) {
      if (sourceKey === null) return { kind: 'none' }
      const source = sourceKey
      const wasDragging = dragging
      reset()
      if (!wasDragging) return { kind: 'click' }
      if (hoverKey === null || hoverKey === source) return { kind: 'abort' }
      return { kind: 'drop', sourceKey: source, targetKey: hoverKey }
    },
    cancel: reset,
    get state() {
      return currentState()
    },
    get dragging() {
      return dragging
    },
    get active() {
      return sourceKey !== null
    },
  }
}

/** 拖過之後 click 若沒有在這段時間內來，抑制自己過期，避免之後的鍵盤 Enter／Space 被誤吞。 */
export const CLICK_SUPPRESSION_MS = 100

export interface ClickSuppression {
  /** 放下／放在格子外：放開當下就知道 click 馬上來，從 `now` 起算 TTL。 */
  suppressFor: (now: number) => void
  /** Esc 取消：使用者可能還按著，click 要等放開那一下才來，所以先只標記「等 pointerup」。 */
  suppressAfterRelease: () => void
  /** 收到 pointerup／pointercancel；若在等放開，TTL 從這一刻起算（不是從 Esc 起算）。 */
  released: (now: number) => void
  /** 新的 pointerdown：前一次的抑制作廢。 */
  clear: () => void
  /** 格子的 `@click` 問；吞掉就清旗標，所以只吞一次。 */
  consume: (now: number) => boolean
}

/** 純邏輯、時間由呼叫端傳入（不用計時器），好測 Esc 隔很久才放開的情境。 */
export function createClickSuppression(ttlMs = CLICK_SUPPRESSION_MS): ClickSuppression {
  let awaitingRelease = false
  let until = 0
  return {
    suppressFor(now) {
      awaitingRelease = false
      until = now + ttlMs
    },
    suppressAfterRelease() {
      awaitingRelease = true
      until = 0
    },
    released(now) {
      if (!awaitingRelease) return
      awaitingRelease = false
      until = now + ttlMs
    },
    clear() {
      awaitingRelease = false
      until = 0
    },
    consume(now) {
      if (now >= until) return false
      until = 0
      return true
    },
  }
}

export interface UsePointerDragSwapOptions {
  /** 這一格能不能當來源（例如空格不能、寫入進行中不能）。回 false 時完全不起手，保留原本的點擊。 */
  canStart: (key: string) => boolean
  /** 放在有效目標上。 */
  onDrop: (sourceKey: string, targetKey: string) => void
  /** 格子鍵的 data 屬性名稱，預設 `data-swap-key`。 */
  attribute?: string
  threshold?: number
}

export interface PointerDragSwapHandlers {
  /** 目前拖曳狀態（來源、目標），給格子畫預覽樣式；沒在拖為 null。 */
  state: ShallowRef<DragState | null>
  onCellPointerDown: (event: PointerEvent, key: string) => void
  onPointerMove: (event: PointerEvent) => void
  onPointerUp: (event: PointerEvent) => void
  onPointerCancel: (event: PointerEvent) => void
  /** 格子的 `@click` 先問這個：剛拖過（放下或 Esc 取消）時回 true 並清掉旗標，呼叫端就跳過開面板。 */
  consumeClickSuppression: () => boolean
}

export function usePointerDragSwap(options: UsePointerDragSwapOptions): PointerDragSwapHandlers {
  const attribute = options.attribute ?? 'data-swap-key'
  const session = createDragSession(options.threshold)
  const state = shallowRef<DragState | null>(null)
  const suppression = createClickSuppression()

  function hoverKeyAt(x: number, y: number): string | null {
    return resolvePaintKey(document.elementFromPoint(x, y), attribute)
  }

  function onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') return
    const wasDragging = session.dragging
    session.cancel()
    state.value = null
    removeKeyListener()
    // 取消拖曳之後使用者放開滑鼠仍會產生 click，不能讓它開出候選人面板；
    // 但使用者可能還按著很久才放，抑制要等 finish() 收到那次放開才開始算。
    if (wasDragging) suppression.suppressAfterRelease()
  }

  function addKeyListener(): void {
    window.addEventListener('keydown', onKeydown)
  }

  function removeKeyListener(): void {
    window.removeEventListener('keydown', onKeydown)
  }

  function onCellPointerDown(event: PointerEvent, key: string): void {
    if (event.button !== 0 || !event.isPrimary) return
    if (!options.canStart(key)) return
    suppression.clear()
    session.start(key, event.clientX, event.clientY)
    ;(event.currentTarget as Element | null)?.setPointerCapture?.(event.pointerId)
    addKeyListener()
  }

  function onPointerMove(event: PointerEvent): void {
    if (!session.active) return
    const next = session.move(event.clientX, event.clientY, hoverKeyAt(event.clientX, event.clientY))
    // 只在內容真的變了才換物件，避免每個 pointermove 都觸發重繪。
    const prev = state.value
    if (prev?.sourceKey !== next?.sourceKey || prev?.overKey !== next?.overKey) state.value = next
  }

  function finish(event: PointerEvent, allowDrop: boolean): void {
    const result = session.end(
      event.clientX,
      event.clientY,
      allowDrop ? hoverKeyAt(event.clientX, event.clientY) : null,
    )
    state.value = null
    removeKeyListener()
    if (result.kind === 'drop' || result.kind === 'abort') suppression.suppressFor(performance.now())
    else suppression.released(performance.now())
    if (result.kind === 'drop') options.onDrop(result.sourceKey, result.targetKey)
  }

  onBeforeUnmount(() => {
    removeKeyListener()
  })

  return {
    state,
    onCellPointerDown,
    onPointerMove,
    onPointerUp: (event) => finish(event, true),
    // 被系統手勢搶走（pointercancel）一律當取消，不對調。
    onPointerCancel: (event) => finish(event, false),
    consumeClickSuppression() {
      return suppression.consume(performance.now())
    },
  }
}
