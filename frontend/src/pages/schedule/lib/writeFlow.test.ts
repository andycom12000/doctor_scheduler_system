import { describe, expect, it } from 'vitest'
import { ApiError } from '@/api/client'
import type { StaffDirectoryEntry } from './scheduleGrid'
import {
  describeCarryOver,
  exportFileName,
  exportLayoutFor,
  isHardViolationsPresent,
  needsPublishedEditConfirm,
  parseSwapCellKey,
  swapCellKey,
} from './writeFlow'

const errorBody = (code: string) => ({ error: { code, message: 'x' } })

describe('isHardViolationsPresent', () => {
  it('409 且 error.code 是 HARD_VIOLATIONS_PRESENT 才算', () => {
    expect(isHardViolationsPresent(new ApiError(409, errorBody('HARD_VIOLATIONS_PRESENT'), 'm'))).toBe(true)
  })

  it('其他狀態碼、其他錯誤碼、非 ApiError、本體形狀不對都不算', () => {
    expect(isHardViolationsPresent(new ApiError(409, errorBody('STAFF_ALREADY_ON_DUTY'), 'm'))).toBe(false)
    expect(isHardViolationsPresent(new ApiError(422, errorBody('HARD_VIOLATIONS_PRESENT'), 'm'))).toBe(false)
    expect(isHardViolationsPresent(new ApiError(409, null, 'm'))).toBe(false)
    expect(isHardViolationsPresent(new ApiError(409, { error: null }, 'm'))).toBe(false)
    expect(isHardViolationsPresent(new Error('x'))).toBe(false)
  })
})

describe('needsPublishedEditConfirm', () => {
  it('已發布且本次還沒確認過才要問', () => {
    expect(needsPublishedEditConfirm('published', false)).toBe(true)
    expect(needsPublishedEditConfirm('published', true)).toBe(false)
  })

  it('草稿或還沒載入都不問', () => {
    expect(needsPublishedEditConfirm('draft', false)).toBe(false)
    expect(needsPublishedEditConfirm(undefined, false)).toBe(false)
  })
})

describe('exportLayoutFor', () => {
  it('日 × 人用 day-by-staff；區域 × 日與單日詳表用 area-by-day', () => {
    expect(exportLayoutFor('day-by-staff')).toBe('day-by-staff')
    expect(exportLayoutFor('area-by-day')).toBe('area-by-day')
    expect(exportLayoutFor('day-detail')).toBe('area-by-day')
  })
})

describe('exportFileName', () => {
  it('優先用回應給的檔名', () => {
    expect(exportFileName('duty-2026-09.xlsx', '2026-09')).toBe('duty-2026-09.xlsx')
  })

  it('百分比編碼的檔名（filename*=UTF-8\'\'）解碼', () => {
    expect(exportFileName('%E7%8F%AD%E8%A1%A8-2026-09.xlsx', '2026-09')).toBe('班表-2026-09.xlsx')
  })

  it('沒有檔名、空白、解碼失敗時的退路', () => {
    expect(exportFileName(null, '2026-09')).toBe('duty-2026-09.xlsx')
    expect(exportFileName('  ', '2026-09')).toBe('duty-2026-09.xlsx')
    expect(exportFileName('100%.xlsx', '2026-09')).toBe('100%.xlsx')
  })
})

describe('describeCarryOver', () => {
  const directory = new Map<string, StaffDirectoryEntry>([
    ['staff-001', { name: '王小明', groupIndex: 0 } as StaffDirectoryEntry],
    ['staff-002', { name: '李小華', groupIndex: 0 } as StaffDirectoryEntry],
  ])

  it('補上姓名，偏移大的在前，同分依 staffId', () => {
    const lines = describeCarryOver(
      [
        { staffId: 'staff-002', points: 0 },
        { staffId: 'staff-001', points: 3 },
        { staffId: 'staff-009', points: 3 },
      ],
      directory,
    )
    expect(lines.map((l) => [l.name, l.points])).toEqual([
      ['王小明', 3],
      ['009', 3],
      ['李小華', 0],
    ])
  })

  it('沒有 carryOver（可省略欄位）回空陣列', () => {
    expect(describeCarryOver(undefined, directory)).toEqual([])
  })
})

describe('swapCellKey', () => {
  it('組合與解析互為反函式', () => {
    expect(parseSwapCellKey(swapCellKey('area-a', '2026-09-05'))).toEqual({ areaId: 'area-a', date: '2026-09-05' })
  })

  it('格式不對回 null', () => {
    expect(parseSwapCellKey('nope')).toBeNull()
    expect(parseSwapCellKey('|2026-09-05')).toBeNull()
    expect(parseSwapCellKey('a|')).toBeNull()
  })
})
