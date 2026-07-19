import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { nearLiveFinanceQueryOptions } from "../../lib/api/liveQueryOptions";
import { queryKeys } from "../../lib/api/queryKeys";
import type { TransactionPageRequest } from "../../types/api";
import { getTransactionPage } from "./transactionsApi";

export function useTransactionPageQuery(request: TransactionPageRequest = {}) {
  return useQuery({
    queryKey: queryKeys.transactions.page(request),
    queryFn: () => getTransactionPage(request),
    ...nearLiveFinanceQueryOptions
  });
}

// Cursor-continued browsing over the canonical paged contract. Pages accumulate
// as the user scrolls; the metadata mutation's transactions-root invalidation
// refreshes them like every other transactions query.
export function useTransactionPagesInfiniteQuery(
  request: Omit<TransactionPageRequest, "cursor"> = {}
) {
  return useInfiniteQuery({
    queryKey: queryKeys.transactions.pages(request),
    queryFn: ({ pageParam }) => getTransactionPage({ ...request, cursor: pageParam }),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    ...nearLiveFinanceQueryOptions
  });
}
