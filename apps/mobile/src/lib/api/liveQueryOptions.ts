export const nearLiveFinanceQueryOptions = {
  staleTime: 30_000,
  refetchOnMount: false,
  refetchOnWindowFocus: false,
  refetchOnReconnect: true,
  refetchInterval: false as const,
  refetchIntervalInBackground: false
};
