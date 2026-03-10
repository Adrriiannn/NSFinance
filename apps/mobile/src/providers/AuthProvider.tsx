import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { AppState, Platform, type AppStateStatus } from "react-native";
import * as SecureStore from "expo-secure-store";
import {
  getCurrentUser,
  logout as logoutApi,
  refreshToken as refreshTokenApi
} from "../features/auth/authApi";
import {
  setApiTokenResolver,
  setApiUnauthorizedHandler
} from "../lib/api/client";
import type { AuthTokenResponse, UserProfileDto } from "../types/api";
import { queryClient } from "./QueryProvider";

const SESSION_KEY = "nsfinance.auth.session";
const INACTIVITY_TIMEOUT_MS = 10 * 60 * 1000;

type StoredSession = {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  sessionId: string;
  user: UserProfileDto;
};

type AuthContextValue = {
  isBootstrapping: boolean;
  isAuthenticated: boolean;
  session: StoredSession | null;
  sessionMessage: string | null;
  applyAuthTokenResponse: (response: AuthTokenResponse) => Promise<void>;
  logout: (reason?: string) => Promise<void>;
  clearSessionMessage: () => void;
  notifyUserInteraction: () => void;
};

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

type AuthProviderProps = {
  children: React.ReactNode;
};

export function AuthProvider({ children }: AuthProviderProps) {
  const [isBootstrapping, setIsBootstrapping] = useState(true);
  const [session, setSession] = useState<StoredSession | null>(null);
  const [sessionMessage, setSessionMessage] = useState<string | null>(null);
  const accessTokenRef = useRef<string | null>(null);
  const sessionRef = useRef<StoredSession | null>(null);
  const refreshPromiseRef = useRef<Promise<string | null> | null>(null);
  const inactivityTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const backgroundedAtRef = useRef<number | null>(null);

  const stopInactivityTimer = useCallback(() => {
    if (inactivityTimerRef.current) {
      clearTimeout(inactivityTimerRef.current);
      inactivityTimerRef.current = null;
    }
  }, []);

  const clearSessionStorage = useCallback(async () => {
    try {
      await SecureStore.deleteItemAsync(SESSION_KEY);
    } catch {
      // Ignore secure store cleanup failures during logout.
    }
  }, []);

  const persistSession = useCallback(async (nextSession: StoredSession | null) => {
    if (!nextSession) {
      await clearSessionStorage();
      return;
    }

    await SecureStore.setItemAsync(SESSION_KEY, JSON.stringify(nextSession));
  }, [clearSessionStorage]);

  const logout = useCallback(
    async (reason?: string) => {
      stopInactivityTimer();
      refreshPromiseRef.current = null;

      const hadSession = Boolean(accessTokenRef.current);
      const tokenBeforeClear = accessTokenRef.current;
      accessTokenRef.current = null;
      sessionRef.current = null;
      setSession(null);
      setApiTokenResolver(() => accessTokenRef.current);
      await clearSessionStorage();
      queryClient.clear();

      if (reason) {
        setSessionMessage(reason);
      }

      if (hadSession && tokenBeforeClear) {
        setApiTokenResolver(() => tokenBeforeClear);
        try {
          await logoutApi();
        } catch {
          // Logout endpoint is best-effort in case token already expired.
        } finally {
          setApiTokenResolver(() => accessTokenRef.current);
        }
      }
    },
    [clearSessionStorage, stopInactivityTimer]
  );

  const startInactivityTimer = useCallback(() => {
    stopInactivityTimer();

    inactivityTimerRef.current = setTimeout(() => {
      void logout("Session expired due to inactivity.");
    }, INACTIVITY_TIMEOUT_MS);
  }, [logout, stopInactivityTimer]);

  const notifyUserInteraction = useCallback(() => {
    if (!accessTokenRef.current) {
      return;
    }

    startInactivityTimer();
  }, [startInactivityTimer]);

  const applyAuthTokenResponse = useCallback(
    async (response: AuthTokenResponse) => {
      const nextSession: StoredSession = {
        accessToken: response.accessToken,
        accessTokenExpiresAtUtc: response.accessTokenExpiresAtUtc,
        refreshToken: response.refreshToken,
        refreshTokenExpiresAtUtc: response.refreshTokenExpiresAtUtc,
        sessionId: response.sessionId,
        user: response.user
      };

      accessTokenRef.current = response.accessToken;
      sessionRef.current = nextSession;
      setSession(nextSession);
      setApiTokenResolver(() => accessTokenRef.current);
      setSessionMessage(null);
      await persistSession(nextSession);
      startInactivityTimer();
    },
    [persistSession, startInactivityTimer]
  );

  const refreshAccessToken = useCallback(async () => {
    if (refreshPromiseRef.current) {
      return refreshPromiseRef.current;
    }

    const runRefresh = (async (): Promise<string | null> => {
      const current = sessionRef.current;
      if (!current) {
        return null;
      }

      const refreshExpiry = Date.parse(current.refreshTokenExpiresAtUtc);
      if (Number.isNaN(refreshExpiry) || refreshExpiry <= Date.now()) {
        await logout("Session expired. Please sign in again.");
        return null;
      }

      try {
        const refreshed = await refreshTokenApi({
          refreshToken: current.refreshToken,
          deviceContext: {
            platform: Platform.OS
          }
        });
        await applyAuthTokenResponse(refreshed);
        return refreshed.accessToken;
      } catch {
        await logout("Session expired. Please sign in again.");
        return null;
      }
    })();

    refreshPromiseRef.current = runRefresh.finally(() => {
      refreshPromiseRef.current = null;
    });

    return refreshPromiseRef.current;
  }, [applyAuthTokenResponse, logout]);

  useEffect(() => {
    setApiTokenResolver(() => accessTokenRef.current);
    setApiUnauthorizedHandler(() => refreshAccessToken());
    return () => {
      setApiTokenResolver(() => null);
      setApiUnauthorizedHandler(null);
    };
  }, [refreshAccessToken]);

  useEffect(() => {
    const bootstrap = async () => {
      try {
        const raw = await SecureStore.getItemAsync(SESSION_KEY);
        if (!raw) {
          setIsBootstrapping(false);
          return;
        }

        const parsed = JSON.parse(raw) as StoredSession;
        const refreshExpiry = Date.parse(parsed.refreshTokenExpiresAtUtc);
        if (
          !parsed.accessToken ||
          !parsed.refreshToken ||
          Number.isNaN(refreshExpiry) ||
          refreshExpiry <= Date.now()
        ) {
          await clearSessionStorage();
          setIsBootstrapping(false);
          return;
        }

        accessTokenRef.current = parsed.accessToken;
        sessionRef.current = parsed;
        setSession(parsed);
        setApiTokenResolver(() => accessTokenRef.current);

        const accessExpiry = Date.parse(parsed.accessTokenExpiresAtUtc);
        if (Number.isNaN(accessExpiry) || accessExpiry <= Date.now()) {
          const refreshedToken = await refreshAccessToken();
          if (!refreshedToken) {
            setIsBootstrapping(false);
            return;
          }
        }

        const currentUser = await getCurrentUser();
        const nextSession = {
          ...(sessionRef.current ?? parsed),
          user: currentUser
        };

        sessionRef.current = nextSession;
        setSession(nextSession);
        await persistSession(nextSession);
        startInactivityTimer();
      } catch {
        await clearSessionStorage();
        accessTokenRef.current = null;
        sessionRef.current = null;
        setSession(null);
      } finally {
        setIsBootstrapping(false);
      }
    };

    void bootstrap();
  }, [clearSessionStorage, persistSession, refreshAccessToken, startInactivityTimer]);

  useEffect(() => {
    const subscription = AppState.addEventListener(
      "change",
      (nextState: AppStateStatus) => {
        if (!accessTokenRef.current) {
          return;
        }

        if (nextState === "active") {
          const backgroundedAt = backgroundedAtRef.current;
          backgroundedAtRef.current = null;

          if (backgroundedAt && Date.now() - backgroundedAt >= INACTIVITY_TIMEOUT_MS) {
            void logout("Session expired due to inactivity.");
            return;
          }

          startInactivityTimer();
          return;
        }

        if (nextState === "background" || nextState === "inactive") {
          backgroundedAtRef.current = Date.now();
          stopInactivityTimer();
        }
      }
    );

    return () => subscription.remove();
  }, [logout, startInactivityTimer, stopInactivityTimer]);

  useEffect(() => {
    if (!session) {
      stopInactivityTimer();
      return;
    }

    startInactivityTimer();
  }, [session, startInactivityTimer, stopInactivityTimer]);

  const value = useMemo<AuthContextValue>(
    () => ({
      isBootstrapping,
      isAuthenticated: Boolean(session),
      session,
      sessionMessage,
      applyAuthTokenResponse,
      logout,
      clearSessionMessage: () => setSessionMessage(null),
      notifyUserInteraction
    }),
    [
      applyAuthTokenResponse,
      isBootstrapping,
      logout,
      notifyUserInteraction,
      session,
      sessionMessage
    ]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuthSession() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuthSession must be used within AuthProvider.");
  }

  return context;
}
