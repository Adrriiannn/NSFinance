import { useQuery } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import { getInsightCategoryBreakdown } from "./insightsApi";

// Server-authoritative per-month category spend (INS-001 + CAT-001): the
// register's category-bars block. The uncategorized remainder ships in the
// contract so the surface can stay honest about coverage.
export function useInsightCategoryBreakdownQuery(months?: number) {
  return useQuery({
    queryKey: queryKeys.insights.categories(months),
    queryFn: () => getInsightCategoryBreakdown(months),
    ...nearLiveFinanceQueryOptions
  });
}
