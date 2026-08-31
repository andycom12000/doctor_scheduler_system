import { http, HttpResponse } from 'msw'

/**
 * Mock 端點。與 api-contract.yaml 一一對應 —— 契約新增端點時，這裡同步補上，
 * 前端就不需要等 backend 完成即可開發。
 *
 * 目前契約只有 /api/health，領域端點待下一階段規劃。
 */
export const handlers = [
  http.get('/api/health', () => HttpResponse.json({ status: 'ok (mock)' })),
]
