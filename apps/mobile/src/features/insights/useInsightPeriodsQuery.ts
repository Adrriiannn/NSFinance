import { useQuery } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import { getInsightPeriods } from "./insightsApi";

// Server-authoritative monthly income/spend/net series (INS-001). The
// Insights surface adopts this as its aggregate source during the UX-003
// overhaul; client-computed comparisons retire at that point.
export function useInsightPeriodsQuery(months?: number) {
  return useQuery({
    queryKey: queryKeys.insights.periods(months),
    queryFn: () => getInsightPeriods(months),
    ...nearLiveFinanceQueryOptions
  });
}
