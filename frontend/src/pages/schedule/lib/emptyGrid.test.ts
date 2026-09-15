import { describe, expect, it } from 'vitest'
import {
  computePanelGeometry,
  emptyStateTitle,
  groupBandsOf,
  groupBoundaryColumns,
  orderStaffByGroup,
  panelRectStyle,
  type EmptyGridColumn,
} from './emptyGrid'
import type { Rank, RankGroup, Staff } from '@/api/types'

const groups: RankGroup[] = [
  { code: 'JUNIOR', name: '低年級' },
  { code: 'MID', name: '中階' },
  { code: 'SENIOR', name: '資深' },
  { code: 'NP', name: 'NP' },
]

const ranks: Rank[] = [
  { code: 'PGY1', name: 'PGY1', groupCode: 'JUNIOR', quotaCap: 10, pointType: 'A' },
  { code: 'PGY2', name: 'PGY2', groupCode: 'JUNIOR', quotaCap: 9, pointType: 'A' },
  { code: 'R2', name: 'R2', groupCode: 'MID', quotaCap: 8, pointType: 'A' },
  { code: 'R4', name: 'R4', groupCode: 'SENIOR', quotaCap: 6, pointType: 'B' },
  { code: 'NP', name: 'NP', groupCode: 'NP', quotaCap: null, pointType: null },
]

function staff(id: string, name: string, rankCode: string): Staff {
  return { id, employeeNo: `E${id}`, name, rankCode, status: 'active', eligibleAreaTypes: [] }
}

describe('orderStaffByGroup', () => {
  it('依組別順序、組內依身分順序、最後依姓名排序', () => {
    const input: Staff[] = [
      staff('s-np', '游芷若', 'NP'),
      staff('s-r4', '賴怡君', 'R4'),
      staff('s-pgy2b', '張三', 'PGY2'),
      staff('s-r2', '許志明', 'R2'),
      staff('s-pgy2a', '王五', 'PGY2'),
      staff('s-pgy1', '陳建宏', 'PGY1'),
    ]
    const ordered = orderStaffByGroup(input, ranks, groups)
    // 組內同身分（PGY2）依姓名排序：JS 預設字串比較是 code point，不是拼音，
    // 「張」(U+5F35) < 「王」(U+738B)，所以 s-pgy2b（張三）排在 s-pgy2a（王五）前面。
    expect(ordered.map((c) => c.staffId)).toEqual(['s-pgy1', 's-pgy2b', 's-pgy2a', 's-r2', 's-r4', 's-np'])
    expect(ordered.map((c) => c.groupIndex)).toEqual([0, 0, 0, 1, 2, 3])
  })

  it('查不到身分組的人員歸到最後一組，不打散色帶總寬', () => {
    const input: Staff[] = [staff('s-1', '陳建宏', 'PGY1'), staff('s-unknown', '未知身分', 'GHOST')]
    const ordered = orderStaffByGroup(input, ranks, groups)
    expect(ordered.find((c) => c.staffId === 's-unknown')?.groupIndex).toBe(3)
  })
})

describe('groupBandsOf', () => {
  it('色帶標籤是位置序 G1…G4，寬度＝組內人數，只列有人的組', () => {
    const columns: EmptyGridColumn[] = [
      { staffId: 'a', name: 'A', rankCode: 'PGY1', groupCode: 'JUNIOR', groupIndex: 0 },
      { staffId: 'b', name: 'B', rankCode: 'PGY1', groupCode: 'JUNIOR', groupIndex: 0 },
      { staffId: 'c', name: 'C', rankCode: 'R4', groupCode: 'SENIOR', groupIndex: 2 },
    ]
    expect(groupBandsOf(columns)).toEqual([
      { label: 'G1', count: 2, groupIndex: 0 },
      { label: 'G3', count: 1, groupIndex: 2 },
    ])
  })

  it('34 人參考名冊：G1 13、G2 7、G3 13、G4 1', () => {
    const seeds: [string, string][] = [
      ...Array(2).fill('PGY1'),
      ...Array(4).fill('PGY2'),
      ...Array(4).fill('R1'),
      ...Array(3).fill('R2'),
      ...Array(4).fill('R3'),
      ...Array(4).fill('R4'),
      ...Array(5).fill('R5'),
      ...Array(4).fill('R6'),
      ...Array(3).fill('PTR'),
      ...Array(1).fill('NP'),
    ].map((rankCode, i) => [`s-${i}`, rankCode])
    const fullRanks: Rank[] = [
      { code: 'PGY1', name: 'PGY1', groupCode: 'JUNIOR', quotaCap: 10, pointType: 'A' },
      { code: 'PGY2', name: 'PGY2', groupCode: 'JUNIOR', quotaCap: 9, pointType: 'A' },
      { code: 'R1', name: 'R1', groupCode: 'JUNIOR', quotaCap: 9, pointType: 'A' },
      { code: 'R2', name: 'R2', groupCode: 'MID', quotaCap: 8, pointType: 'A' },
      { code: 'R3', name: 'R3', groupCode: 'MID', quotaCap: 7, pointType: 'A' },
      { code: 'R4', name: 'R4', groupCode: 'SENIOR', quotaCap: 6, pointType: 'B' },
      { code: 'R5', name: 'R5', groupCode: 'SENIOR', quotaCap: 5, pointType: 'B' },
      { code: 'R6', name: 'R6', groupCode: 'SENIOR', quotaCap: 5, pointType: 'B' },
      { code: 'PTR', name: '打工R', groupCode: 'JUNIOR', quotaCap: 6, pointType: 'A' },
      { code: 'NP', name: 'NP', groupCode: 'NP', quotaCap: null, pointType: null },
    ]
    const staffList = seeds.map(([id, rankCode]) => staff(id, id, rankCode))
    const columns = orderStaffByGroup(staffList, fullRanks, groups)
    expect(columns).toHaveLength(34)
    expect(groupBandsOf(columns)).toEqual([
      { label: 'G1', count: 13, groupIndex: 0 },
      { label: 'G2', count: 7, groupIndex: 1 },
      { label: 'G3', count: 13, groupIndex: 2 },
      { label: 'G4', count: 1, groupIndex: 3 },
    ])
  })
})

describe('groupBoundaryColumns', () => {
  it('每個身分組的第一欄（含第 0 欄）是交界欄', () => {
    const columns: EmptyGridColumn[] = [
      { staffId: 'a', name: 'A', rankCode: 'PGY1', groupCode: 'JUNIOR', groupIndex: 0 },
      { staffId: 'b', name: 'B', rankCode: 'PGY1', groupCode: 'JUNIOR', groupIndex: 0 },
      { staffId: 'c', name: 'C', rankCode: 'R2', groupCode: 'MID', groupIndex: 1 },
      { staffId: 'd', name: 'D', rankCode: 'R4', groupCode: 'SENIOR', groupIndex: 2 },
    ]
    expect(groupBoundaryColumns(columns)).toEqual(new Set([0, 2, 3]))
  })
})

describe('computePanelGeometry', () => {
  it('33 欄 × 30 列 → colSpan 12 colStart 11、rowSpan 10 rowStart 10（規格 §5.2 範例）', () => {
    expect(computePanelGeometry(33, 30)).toEqual({ colSpan: 12, colStart: 11, rowSpan: 10, rowStart: 10 })
  })

  it('34 欄 × 31 列', () => {
    expect(computePanelGeometry(34, 31)).toEqual({
      colSpan: Math.round(34 * 0.36),
      colStart: Math.round((34 * 0.64) / 2),
      rowSpan: Math.round(31 * 0.34),
      rowStart: Math.round((31 * 0.66) / 2),
    })
  })
})

describe('panelRectStyle', () => {
  it('用 calc() 表示，含固定尺寸 44px／118px 與格數比例', () => {
    const rect = panelRectStyle(33, 30)
    expect(rect.left).toBe('calc(44px + (100% - 44px) * 11 / 33)')
    expect(rect.top).toBe('calc(118px + (100% - 118px) * 10 / 30)')
    expect(rect.width).toBe('calc((100% - 44px) * 12 / 33)')
    expect(rect.height).toBe('calc((100% - 118px) * 10 / 30)')
  })

  it('欄數或列數為 0（資料還沒載完）時回傳零尺寸，不算出 NaN', () => {
    expect(panelRectStyle(0, 30)).toEqual({ left: '0px', top: '0px', width: '0px', height: '0px' })
    expect(panelRectStyle(33, 0)).toEqual({ left: '0px', top: '0px', width: '0px', height: '0px' })
  })
})

describe('emptyStateTitle', () => {
  it('格式「{YYYY} 年 {M} 月尚未產生班表」，月份不補零', () => {
    expect(emptyStateTitle('2026-09')).toBe('2026 年 9 月尚未產生班表')
    expect(emptyStateTitle('2026-11')).toBe('2026 年 11 月尚未產生班表')
  })
})
