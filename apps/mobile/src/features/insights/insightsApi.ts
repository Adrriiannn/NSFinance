import { apiRequest } from "../../lib/api/client";
import type { InsightPeriodsDto } from "../../types/api";

export function getInsightPeriods(months?: number): Promise<InsightPeriodsDto> {
  const suffix = typeof months === "number" ? `?months=${months}` : "";
  return apiRequest<InsightPeriodsDto>(`/api/insights/periods${suffix}`);
}
