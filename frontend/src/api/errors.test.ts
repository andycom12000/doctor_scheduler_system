import { describe, expect, it } from 'vitest'
import { ApiError } from './client'
import { describeError } from './errors'
import type { ErrorCode } from './types'

/** 契約 `ErrorCode` 列舉的 13 個值（api-contract.yaml components.schemas.ErrorCode）。 */
const ALL_ERROR_CODES: ErrorCode[] = [
  'NOT_FOUND',
  'INVALID_REQUEST',
  'BLOCKED_DAY_CAP_EXCEEDED',
  'HARD_VIOLATIONS_PRESENT',
  'SCHEDULE_ALREADY_PUBLISHED',
  'STAFF_ALREADY_ON_DUTY',
  'AREA_IN_USE',
  'AREA_TYPE_IN_USE',
  'RANK_IN_USE',
  'EMPLOYEE_NO_TAKEN',
  'STAFF_HAS_DUTIES',
  'SOLVER_BUSY',
  'SOLVER_FAILED',
]

function apiErrorOf(code: ErrorCode): ApiError {
  return new ApiError(422, { error: { code, message: 'mock', details: null } }, 'mock')
}

describe('describeError', () => {
  it.each(ALL_ERROR_CODES)('%s 有非空的中文訊息', (code) => {
    const message = describeError(apiErrorOf(code))
    expect(typeof message).toBe('string')
    expect(message.length).toBeGreaterThan(0)
  })

  it('每個錯誤碼的訊息都不相同', () => {
    const messages = new Set(ALL_ERROR_CODES.map((code) => describeError(apiErrorOf(code))))
    expect(messages.size).toBe(ALL_ERROR_CODES.length)
  })

  it('ApiError 但 body 不是預期形狀時退回 err.message', () => {
    const err = new ApiError(500, { unexpected: true }, 'GET /api/x → 500')
    expect(describeError(err)).toBe('GET /api/x → 500')
  })

  it('ApiError 的 code 不在對照表裡時退回 err.message', () => {
    const err = new ApiError(400, { error: { code: 'SOMETHING_NEW' } }, 'boom')
    expect(describeError(err)).toBe('boom')
  })

  it('一般 Error 回它自己的 message', () => {
    expect(describeError(new Error('network down'))).toBe('network down')
  })

  it('非 Error 值回一般性訊息', () => {
    expect(describeError('just a string')).toBe('發生未預期的錯誤，請稍後再試。')
    expect(describeError(null)).toBe('發生未預期的錯誤，請稍後再試。')
  })
})
