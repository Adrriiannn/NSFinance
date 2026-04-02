import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as SecureStore from "expo-secure-store";
import {
  getCurrentUser,
  logout as logoutApi,
  refreshToken as refreshTokenApi
} from "../features/auth/authApi";
import { resetGoogleOAuthFlowState } from "../features/auth/googleOAuthFlowState";
import {
  setApiTokenResolver,
  setApiUnauthorizedHandler
} from "../lib/api/client";
import { buildDeviceContext } from "../lib/device/deviceIdentity";
import type { AuthTokenResponse, UserProfileDto } from "../types/api";
import { queryClient } from "./QueryProvider";

const SESSION_KEY = "nsfinance.auth.session";

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
  isAuthTransitioning: boolean;
  isAuthenticated: boolean;
  session: StoredSession | null;
  sessionMessage: string | null;
  applyAuthTokenResponse: (response: AuthTokenResponse) => Promise<void>;
  refreshSessionUser: () => Promise<void>;
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
  const [isAuthTransitioning, setIsAuthTransitioning] = useState(false);
  const [session, setSession] = useState<StoredSession | null>(null);
  const [sessionMessage, setSessionMessage] = useState<string | null>(null);
  const accessTokenRef = useRef<string | null>(null);
  const sessionRef = useRef<StoredSession | null>(null);
  const refreshPromiseRef = useRef<Promise<string | null> | null>(null);
  const logoutPromiseRef = useRef<Promise<void> | null>(null);

  const logAuthDebug = useCallback((event: string, details?: Record<string, unknown>) => {
    if (!__DEV__) {
      return;
    }

    if (!details) {
      console.info(`[Auth] ${event}`);
      return;
    }

    console.info(`[Auth] ${event}`, details);
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
      if (logoutPromiseRef.current) {
        await logoutPromiseRef.current;
        return;
      }

      const runLogout = async () => {
        setIsAuthTransitioning(true);
        logAuthDebug("logout_started", {
          hasReason: Boolean(reason),
          reason: reason ?? ""
        });
        resetGoogleOAuthFlowState("logout");
        refreshPromiseRef.current = null;

        const hadSession = Boolean(accessTokenRef.current);
        const tokenBeforeClear = accessTokenRef.current;
        accessTokenRef.current = null;
        sessionRef.current = null;
        setSession(null);
        setApiTokenResolver(() => accessTokenRef.current);
        await clearSessionStorage();
        await queryClient.cancelQueries();
        queryClient.clear();
        logAuthDebug("logout_storage_and_cache_cleared");

        if (reason) {
          setSessionMessage(reason);
        }

        if (hadSession && tokenBeforeClear) {
          setApiTokenResolver(() => tokenBeforeClear);
          try {
            await logoutApi();
            logAuthDebug("logout_api_succeeded");
          } catch {
            // Logout endpoint is best-effort in case token already expired.
            logAuthDebug("logout_api_failed");
          } finally {
            setApiTokenResolver(() => accessTokenRef.current);
          }
        }

        logAuthDebug("logout_completed");
      };

      logoutPromiseRef.current = runLogout().finally(() => {
        setIsAuthTransitioning(false);
        logoutPromiseRef.current = null;
      });

      await logoutPromiseRef.current;
    },
    [clearSessionStorage, logAuthDebug]
  );

  const notifyUserInteraction = useCallback(() => {
    // Device-bound sessions are persistent until manual logout/revocation.
    // Keep this method as a no-op for UI hooks that still call it.
  }, []);

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
    },
    [persistSession]
  );

  const refreshSessionUser = useCallback(async () => {
    const currentSession = sessionRef.current;
    if (!currentSession) {
      return;
    }

    try {
      const currentUser = await getCurrentUser();
      const latestSession = sessionRef.current;
      if (!latestSession || latestSession.sessionId !== currentSession.sessionId) {
        return;
      }

      const nextSession = {
        ...latestSession,
        user: currentUser
      };

      sessionRef.current = nextSession;
      setSession(nextSession);
      await persistSession(nextSession);
    } catch {
      // Keep current session user payload if profile refresh fails.
    }
  }, [persistSession]);

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
          deviceContext: buildDeviceContext()
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
  }, [clearSessionStorage, persistSession, refreshAccessToken]);

  const value = useMemo<AuthContextValue>(
    () => ({
      isBootstrapping,
      isAuthTransitioning,
      isAuthenticated: Boolean(session),
      session,
      sessionMessage,
      applyAuthTokenResponse,
      refreshSessionUser,
      logout,
      clearSessionMessage: () => setSessionMessage(null),
      notifyUserInteraction
    }),
    [
      applyAuthTokenResponse,
      refreshSessionUser,
      isBootstrapping,
      isAuthTransitioning,
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
