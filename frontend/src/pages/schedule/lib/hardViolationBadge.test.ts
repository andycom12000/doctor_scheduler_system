import { describe, expect, it } from 'vitest'
import type { Violation } from '@/api/types'
import { doubleBookingBlockMessage, hardViolationBadgeInfo, printBlockMessage } from './hardViolationBadge'

const v = (code: string, severity: 'hard' | 'soft', id = code): Violation => ({
  id,
  code,
  severity,
  cellKeys: ['area:area-a:2026-09-01'],
  message: 'x',
})

describe('hardViolationBadgeInfo', () => {
  it('沒有違規或只有軟違規 → null（標籤不顯示）', () => {
    expect(hardViolationBadgeInfo([])).toBeNull()
    expect(hardViolationBadgeInfo([v('S3_X', 'soft')])).toBeNull()
  })

  it('只有一般硬違規 → 只有第一行，軟違規不算', () => {
    const info = hardViolationBadgeInfo([v('H1_AREA_COVERAGE', 'hard', 'a'), v('H5_BLOCKED_DAY', 'hard', 'b'), v('S3_X', 'soft')])
    expect(info).toEqual({ headline: '2 項硬違規', detail: null, label: '2 項硬違規' })
  })

  it('有同人同日兩區 → 第二行明說排除後才能發布', () => {
    const info = hardViolationBadgeInfo([
      v('H1_AREA_COVERAGE', 'hard', 'a'),
      v('X1_STAFF_DOUBLE_BOOKED', 'hard', 'b'),
      v('X1_STAFF_DOUBLE_BOOKED', 'hard', 'c'),
    ])
    expect(info?.headline).toBe('3 項硬違規')
    expect(info?.detail).toBe('其中 2 項同人同日兩區，排除後才能發布／匯出／列印')
    expect(info?.label).toContain('排除後才能發布')
  })
})

describe('doubleBookingBlockMessage', () => {
  it('有 X1 → 依動作組訊息；沒有（含只有其他硬違規）→ null', () => {
    const x1 = [v('H1_AREA_COVERAGE', 'hard', 'a'), v('X1_STAFF_DOUBLE_BOOKED', 'hard', 'b')]
    expect(doubleBookingBlockMessage(x1, '匯出')).toBe('還有人同一天排在兩區，排除後才能匯出。')
    expect(doubleBookingBlockMessage(x1, '列印')).toBe('還有人同一天排在兩區，排除後才能列印。')
    expect(doubleBookingBlockMessage([v('H1_AREA_COVERAGE', 'hard')], '匯出')).toBeNull()
    expect(doubleBookingBlockMessage([], '列印')).toBeNull()
  })
})

describe('printBlockMessage', () => {
  it('清單拿不到或重抓失敗 → fail-closed 不印', () => {
    expect(printBlockMessage(null, false)).toBe('無法確認違規清單，暫時不能列印，請稍後再試。')
    expect(printBlockMessage([], true)).toBe('無法確認違規清單，暫時不能列印，請稍後再試。')
  })

  it('有 X1 → 擋；沒有 X1（含其他硬違規）→ 放行', () => {
    expect(printBlockMessage([v('X1_STAFF_DOUBLE_BOOKED', 'hard')], false)).toBe('還有人同一天排在兩區，排除後才能列印。')
    expect(printBlockMessage([v('H1_AREA_COVERAGE', 'hard')], false)).toBeNull()
    expect(printBlockMessage([], false)).toBeNull()
  })
})
