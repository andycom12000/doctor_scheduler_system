/** `settings` 標籤：五份整份取代的設定文件，加上逐月覆寫。設定類 GET／PUT 都是薄包裝，不做業務邏輯。 */
import { apiGet, apiPut } from './client'
import type {
  AreaSettings,
  ConstraintSettings,
  EligibilityMatrix,
  MonthlyOverride,
  PointRules,
  RankSettings,
} from './types'

export function getAreaSettings(signal?: AbortSignal): Promise<AreaSettings> {
  return apiGet<AreaSettings>('/settings/areas', { signal })
}

export function putAreaSettings(body: AreaSettings, signal?: AbortSignal): Promise<AreaSettings> {
  return apiPut<AreaSettings>('/settings/areas', body, { signal })
}

export function getRankSettings(signal?: AbortSignal): Promise<RankSettings> {
  return apiGet<RankSettings>('/settings/ranks', { signal })
}

export function putRankSettings(body: RankSettings, signal?: AbortSignal): Promise<RankSettings> {
  return apiPut<RankSettings>('/settings/ranks', body, { signal })
}

export function getEligibilityMatrix(signal?: AbortSignal): Promise<EligibilityMatrix> {
  return apiGet<EligibilityMatrix>('/settings/eligibility-matrix', { signal })
}

export function putEligibilityMatrix(body: EligibilityMatrix, signal?: AbortSignal): Promise<EligibilityMatrix> {
  return apiPut<EligibilityMatrix>('/settings/eligibility-matrix', body, { signal })
}

export function getPointRules(signal?: AbortSignal): Promise<PointRules> {
  return apiGet<PointRules>('/settings/point-rules', { signal })
}

export function putPointRules(body: PointRules, signal?: AbortSignal): Promise<PointRules> {
  return apiPut<PointRules>('/settings/point-rules', body, { signal })
}

export function getConstraints(signal?: AbortSignal): Promise<ConstraintSettings> {
  return apiGet<ConstraintSettings>('/settings/constraints', { signal })
}

export function putConstraints(body: ConstraintSettings, signal?: AbortSignal): Promise<ConstraintSettings> {
  return apiPut<ConstraintSettings>('/settings/constraints', body, { signal })
}

export function getMonthlyOverride(ym: string, signal?: AbortSignal): Promise<MonthlyOverride> {
  return apiGet<MonthlyOverride>(`/settings/monthly-overrides/${ym}`, { signal })
}

export function putMonthlyOverride(
  ym: string,
  body: MonthlyOverride,
  signal?: AbortSignal,
): Promise<MonthlyOverride> {
  return apiPut<MonthlyOverride>(`/settings/monthly-overrides/${ym}`, body, { signal })
}
