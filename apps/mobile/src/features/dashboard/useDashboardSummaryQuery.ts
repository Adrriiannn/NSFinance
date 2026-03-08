import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import { getDashboardSummary } from "./dashboardApi";

export function useDashboardSummaryQuery() {
  return useQuery({
    queryKey: queryKeys.dashboard.summary,
    queryFn: getDashboardSummary
  });
}
