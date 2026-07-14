import { useQuery } from "@tanstack/react-query";
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
