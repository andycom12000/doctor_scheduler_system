/**
 * `solver` 標籤：求解工作的建立／查詢／中止／變體清單。
 * 進度推送不在這裡——SSE／WebView2 host message 的平台分支封裝在 `src/realtime.ts`。
 */
import { apiDelete, apiGet, apiPost } from './client'
import type { CreateSolverJobRequest, ListVariantsResponse, SolverJob } from './types'

export function createSolverJob(body: CreateSolverJobRequest, signal?: AbortSignal): Promise<SolverJob> {
  return apiPost<SolverJob>('/solver-jobs', body, { signal })
}

export function getSolverJob(jobId: string, signal?: AbortSignal): Promise<SolverJob> {
  return apiGet<SolverJob>(`/solver-jobs/${jobId}`, { signal })
}

export function cancelSolverJob(jobId: string, signal?: AbortSignal): Promise<SolverJob> {
  return apiDelete<SolverJob>(`/solver-jobs/${jobId}`, { signal })
}

export function listVariants(jobId: string, signal?: AbortSignal): Promise<ListVariantsResponse> {
  return apiGet<ListVariantsResponse>(`/solver-jobs/${jobId}/variants`, { signal })
}
