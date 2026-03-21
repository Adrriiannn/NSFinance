import {
  dehydrate,
  hydrate,
  QueryClient,
  QueryClientProvider,
  focusManager
} from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { AppState, type AppStateStatus } from "react-native";
import {
  readJsonFileStorage,
  writeJsonFileStorage
} from "../lib/storage/jsonFileStore";

type QueryProviderProps = {
  children: React.ReactNode;
};

type PersistedQueryCache = {
  savedAtMs: number;
  dehydratedState: ReturnType<typeof dehydrate>;
};

const QUERY_CACHE_STORAGE_KEY = "nsfinance.react-query.cache.v1";
const QUERY_CACHE_MAX_AGE_MS = 15 * 60_000;
const QUERY_CACHE_PERSIST_DEBOUNCE_MS = 1_000;

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
  const [isCacheHydrated, setIsCacheHydrated] = useState(false);
  const persistTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    let cancelled = false;

    const hydrateCache = async () => {
      try {
        const persisted = await readJsonFileStorage<PersistedQueryCache>(QUERY_CACHE_STORAGE_KEY);
        if (!persisted || cancelled) {
          return;
        }

        const cacheAgeMs = Date.now() - persisted.savedAtMs;
        if (cacheAgeMs > QUERY_CACHE_MAX_AGE_MS) {
          return;
        }

        hydrate(queryClient, persisted.dehydratedState);
      } catch {
        // Ignore cache hydration issues and continue boot.
      } finally {
        if (!cancelled) {
          setIsCacheHydrated(true);
        }
      }
    };

    void hydrateCache();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!isCacheHydrated) {
      return;
    }

    const schedulePersist = () => {
      if (persistTimerRef.current) {
        clearTimeout(persistTimerRef.current);
      }

      persistTimerRef.current = setTimeout(() => {
        persistTimerRef.current = null;
        const dehydratedState = dehydrate(queryClient, {
          shouldDehydrateQuery: (query) => query.state.status === "success"
        });
        void writeJsonFileStorage(QUERY_CACHE_STORAGE_KEY, {
          savedAtMs: Date.now(),
          dehydratedState
        } satisfies PersistedQueryCache);
      }, QUERY_CACHE_PERSIST_DEBOUNCE_MS);
    };

    const unsubscribeQueryCache = queryClient.getQueryCache().subscribe(schedulePersist);
    const unsubscribeMutationCache = queryClient.getMutationCache().subscribe(schedulePersist);

    return () => {
      unsubscribeQueryCache();
      unsubscribeMutationCache();
      if (persistTimerRef.current) {
        clearTimeout(persistTimerRef.current);
      }
    };
  }, [isCacheHydrated]);

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
      {isCacheHydrated ? children : null}
    </QueryClientProvider>
  );
}

export { queryClient };
