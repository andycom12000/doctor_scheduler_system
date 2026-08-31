import { apiGet } from './client'

/** api-contract.yaml 目前唯一定案的端點。領域端點待下一階段規劃後補上。 */
export interface HealthStatus {
  status: string
}

export function getHealth(signal?: AbortSignal): Promise<HealthStatus> {
  return apiGet<HealthStatus>('/health', { signal })
}
