import { describe, expect, it } from 'vitest'
import { extractBusyJobId } from './solverKickoff'

describe('extractBusyJobId', () => {
  it('從 409 SOLVER_BUSY 的錯誤本體解析 details.jobId', () => {
    const body = { error: { code: 'SOLVER_BUSY', message: '已有求解工作在執行中', details: { jobId: 'job-1' } } }
    expect(extractBusyJobId(body)).toBe('job-1')
  })

  it('形狀不對（缺 error／details／jobId 或型別不符）一律回 null，不拋例外', () => {
    expect(extractBusyJobId(null)).toBeNull()
    expect(extractBusyJobId(undefined)).toBeNull()
    expect(extractBusyJobId('not an object')).toBeNull()
    expect(extractBusyJobId({})).toBeNull()
    expect(extractBusyJobId({ error: null })).toBeNull()
    expect(extractBusyJobId({ error: {} })).toBeNull()
    expect(extractBusyJobId({ error: { details: null } })).toBeNull()
    expect(extractBusyJobId({ error: { details: { jobId: 42 } } })).toBeNull()
  })
})
