import { apiRequest } from "../../lib/api/client";
import type { DashboardSummaryDto } from "../../types/api";

export function getDashboardSummary(): Promise<DashboardSummaryDto> {
  return apiRequest<DashboardSummaryDto>("/api/dashboard/summary");
}

