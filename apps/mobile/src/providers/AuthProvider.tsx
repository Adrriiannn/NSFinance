import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { AppState, type AppStateStatus } from "react-native";
import * as SecureStore from "expo-secure-store";
import { getCurrentUser, logout as logoutApi } from "../features/auth/authApi";
import { setApiTokenResolver } from "../lib/api/client";
import type { AuthTokenResponse, UserProfileDto } from "../types/api";
import { queryClient } from "./QueryProvider";

const SESSION_KEY = "nsfintech.auth.session";
const INACTIVITY_TIMEOUT_MS = 10 * 60 * 1000;

type StoredSession = {
  accessToken: string;
  expiresAtUtc: string;
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

  const logout = useCallback(
    async (reason?: string) => {
      stopInactivityTimer();

      const hadSession = Boolean(accessTokenRef.current);
      const tokenBeforeClear = accessTokenRef.current;
      accessTokenRef.current = null;
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
          // Logout endpoint is best-effort for stateless JWT.
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
        expiresAtUtc: response.expiresAtUtc,
        user: response.user
      };

      accessTokenRef.current = response.accessToken;
      setSession(nextSession);
      setApiTokenResolver(() => accessTokenRef.current);
      setSessionMessage(null);
      await SecureStore.setItemAsync(SESSION_KEY, JSON.stringify(nextSession));
      startInactivityTimer();
    },
    [startInactivityTimer]
  );

  useEffect(() => {
    setApiTokenResolver(() => accessTokenRef.current);
    return () => setApiTokenResolver(() => null);
  }, []);

  useEffect(() => {
    const bootstrap = async () => {
      try {
        const raw = await SecureStore.getItemAsync(SESSION_KEY);
        if (!raw) {
          setIsBootstrapping(false);
          return;
        }

        const parsed = JSON.parse(raw) as StoredSession;
        const expiry = Date.parse(parsed.expiresAtUtc);
        if (!parsed.accessToken || Number.isNaN(expiry) || expiry <= Date.now()) {
          await clearSessionStorage();
          setIsBootstrapping(false);
          return;
        }

        accessTokenRef.current = parsed.accessToken;
        setApiTokenResolver(() => accessTokenRef.current);

        const currentUser = await getCurrentUser();
        const hydratedSession: StoredSession = {
          ...parsed,
          user: currentUser
        };

        setSession(hydratedSession);
        await SecureStore.setItemAsync(SESSION_KEY, JSON.stringify(hydratedSession));
        startInactivityTimer();
      } catch {
        await clearSessionStorage();
        accessTokenRef.current = null;
        setSession(null);
      } finally {
        setIsBootstrapping(false);
      }
    };

    void bootstrap();
  }, [clearSessionStorage, startInactivityTimer]);

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
