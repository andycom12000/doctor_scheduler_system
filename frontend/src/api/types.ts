/**
 * `src/api/schema.d.ts` 是 `npm run api:types` 由 api-contract.yaml 生成的檔案，
 * 不進版控（見 package.json 的 pretypecheck / prebuild / predev / predev:mock hook）。
 *
 * 這裡把常用的 schema 型別攤平成好用的別名，畫面元件與 mock handler 都從這裡取，
 * 不手抄 interface、不直接戳 `components['schemas'][...]`。
 */
import type { components, operations } from './schema.d.ts'

export type HealthStatus = components['schemas']['HealthStatus']

export type ErrorResponse = components['schemas']['ErrorResponse']
export type ErrorCode = components['schemas']['ErrorCode']
export type Severity = components['schemas']['Severity']
export type Metric = components['schemas']['Metric']

export type ScheduleStatus = components['schemas']['ScheduleStatus']
export type ScheduleSummary = components['schemas']['ScheduleSummary']
export type Schedule = components['schemas']['Schedule']
export type Duty = components['schemas']['Duty']
export type SetDutyRequest = components['schemas']['SetDutyRequest']
export type SwapDutiesRequest = components['schemas']['SwapDutiesRequest']
export type CellRef = components['schemas']['CellRef']
export type MutationResult = components['schemas']['MutationResult']
export type ValidationResult = components['schemas']['ValidationResult']
export type PublishResult = components['schemas']['PublishResult']
export type CarryOverEntry = components['schemas']['CarryOverEntry']
export type Violation = components['schemas']['Violation']

export type PointBoardGroup = components['schemas']['PointBoardGroup']
export type PointBoardRow = components['schemas']['PointBoardRow']
export type DayDetail = components['schemas']['DayDetail']
export type VacancyByDate = components['schemas']['VacancyByDate']
export type Candidate = components['schemas']['Candidate']

export type BlockedDayRegistration = components['schemas']['BlockedDayRegistration']
export type BlockedDayEntry = components['schemas']['BlockedDayEntry']
export type BlockedDayMutationResult = components['schemas']['BlockedDayMutationResult']
export type FeasibilityReport = components['schemas']['FeasibilityReport']

export type CreateSolverJobRequest = components['schemas']['CreateSolverJobRequest']
export type SolverJobStatus = components['schemas']['SolverJobStatus']
export type SolverJob = components['schemas']['SolverJob']
export type SolverProgress = components['schemas']['SolverProgress']
export type Variant = components['schemas']['Variant']

export type AreaSettings = components['schemas']['AreaSettings']
export type AreaType = components['schemas']['AreaType']
export type Area = components['schemas']['Area']
export type RankSettings = components['schemas']['RankSettings']
export type Rank = components['schemas']['Rank']
export type RankGroup = components['schemas']['RankGroup']
export type EligibilityMatrix = components['schemas']['EligibilityMatrix']
export type PointRules = components['schemas']['PointRules']
export type ConstraintSettings = components['schemas']['ConstraintSettings']
export type Primitive = components['schemas']['Primitive']
export type HardConstraint = components['schemas']['HardConstraint']
export type SoftConstraint = components['schemas']['SoftConstraint']
export type ConstraintScope = components['schemas']['ConstraintScope']
export type MonthlyOverride = components['schemas']['MonthlyOverride']
export type Calendar = components['schemas']['Calendar']
export type CalendarDay = components['schemas']['CalendarDay']
export type CalendarDayOverride = components['schemas']['CalendarDayOverride']
export type CalendarSyncStatus = components['schemas']['CalendarSyncStatus']

export type StaffStatus = components['schemas']['StaffStatus']
export type StaffCounts = components['schemas']['StaffCounts']
export type Staff = components['schemas']['Staff']
export type StaffWrite = components['schemas']['StaffWrite']

/**
 * 以下是契約裡沒有具名 schema、內嵌在 operation 回應／請求本體裡的形狀。
 * 從 `operations[...]` 攤平出來，避免手抄 interface 跟契約漂移。
 */
export type ListSchedulesResponse = operations['listSchedules']['responses']['200']['content']['application/json']
export type ListViolationsResponse = operations['listViolations']['responses']['200']['content']['application/json']
export type GetPointBoardResponse = operations['getPointBoard']['responses']['200']['content']['application/json']
export type ListVacanciesResponse = operations['listVacancies']['responses']['200']['content']['application/json']
export type ListCandidatesResponse = operations['listCandidates']['responses']['200']['content']['application/json']
export type ListVariantsResponse = operations['listVariants']['responses']['200']['content']['application/json']
export type ListStaffResponse = operations['listStaff']['responses']['200']['content']['application/json']

export type ExportLayout = NonNullable<
  NonNullable<operations['exportSchedule']['parameters']['query']>['layout']
>
export type PublishRequest = NonNullable<operations['publishSchedule']['requestBody']>['content']['application/json']
export type ApplyVariantRequest = operations['applyVariant']['requestBody']['content']['application/json']
export type SetStaffStatusRequest = operations['setStaffStatus']['requestBody']['content']['application/json']
