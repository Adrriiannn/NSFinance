import { useQuery } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import { getDashboardSummary } from "./dashboardApi";

export function useDashboardSummaryQuery() {
  return useQuery({
    queryKey: queryKeys.dashboard.summary,
    queryFn: getDashboardSummary,
    ...nearLiveFinanceQueryOptions
  });
}
