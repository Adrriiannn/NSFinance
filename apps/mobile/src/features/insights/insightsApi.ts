import { apiRequest } from "../../lib/api/client";
import type { InsightCategoryBreakdownDto, InsightPeriodsDto } from "../../types/api";

export function getInsightPeriods(months?: number): Promise<InsightPeriodsDto> {
  const suffix = typeof months === "number" ? `?months=${months}` : "";
  return apiRequest<InsightPeriodsDto>(`/api/insights/periods${suffix}`);
}

export function getInsightCategoryBreakdown(months?: number): Promise<InsightCategoryBreakdownDto> {
  const suffix = typeof months === "number" ? `?months=${months}` : "";
  return apiRequest<InsightCategoryBreakdownDto>(`/api/insights/categories${suffix}`);
}
