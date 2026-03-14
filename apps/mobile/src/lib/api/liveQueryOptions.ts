export const nearLiveFinanceQueryOptions = {
  staleTime: 5_000,
  refetchOnMount: "always" as const,
  refetchOnWindowFocus: "always" as const,
  refetchOnReconnect: "always" as const,
  refetchInterval: 15_000,
  refetchIntervalInBackground: false
};
