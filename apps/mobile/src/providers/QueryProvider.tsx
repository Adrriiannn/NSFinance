import { QueryClient, QueryClientProvider, focusManager } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { AppState, type AppStateStatus } from "react-native";
import { deleteAmbiguousLegacyMobileStorage } from "../lib/storage/mobileStorageLifecycle";

type QueryProviderProps = {
  children: React.ReactNode;
};

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 10 * 60_000,
      retry: 1,
      refetchOnWindowFocus: false,
      refetchOnReconnect: true
    },
    mutations: {
      retry: 0
    }
  }
});

export function QueryProvider({ children }: QueryProviderProps) {
  const [isStoragePrepared, setIsStoragePrepared] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const prepareStorage = async () => {
      try {
        await clearAccountQueryState();
      } catch {
        // Storage cleanup is best effort; no legacy cache is ever hydrated.
      } finally {
        if (!cancelled) {
          setIsStoragePrepared(true);
        }
      }
    };

    void prepareStorage();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const subscription = AppState.addEventListener(
      "change",
      (status: AppStateStatus) => {
        focusManager.setFocused(status === "active");
      }
    );

    return () => subscription.remove();
  }, []);

  return (
    <QueryClientProvider client={queryClient}>
      {isStoragePrepared ? children : null}
    </QueryClientProvider>
  );
}

export async function clearAccountQueryState(): Promise<void> {
  const cancellation = queryClient.cancelQueries();
  queryClient.clear();
  await cancellation;
  await deleteAmbiguousLegacyMobileStorage();
}

export { queryClient };
