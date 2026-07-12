import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as SecureStore from "expo-secure-store";
import { AppState, type AppStateStatus } from "react-native";
import {
  getCurrentUser,
  logout as logoutApi,
  refreshToken as refreshTokenApi
} from "../features/auth/authApi";
import { clearNativeGoogleSignInState } from "../features/auth/googleNativeSignIn";
import {
  authenticateWithBiometrics,
  getBiometricAvailability,
  readBiometricPreference,
  writeBiometricPreference
} from "../features/auth/biometricSecurity";
import { clearPendingAuthFlows } from "../features/auth/pendingAuthFlow";
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
  isAppLocked: boolean;
  biometricEnabled: boolean;
  biometricAvailable: boolean;
  biometricLabel: string;
  biometricFailureCount: number;
  shouldOfferBiometrics: boolean;
  session: StoredSession | null;
  sessionMessage: string | null;
  applyAuthTokenResponse: (response: AuthTokenResponse, rememberMe?: boolean) => Promise<void>;
  refreshSessionUser: () => Promise<void>;
  unlockWithBiometrics: () => Promise<{ succeeded: boolean; message?: string }>;
  enableBiometrics: () => Promise<{ succeeded: boolean; message?: string }>;
  disableBiometrics: () => Promise<void>;
  declineBiometrics: () => Promise<void>;
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
  const [isAppLocked, setIsAppLocked] = useState(false);
  const [biometricEnabled, setBiometricEnabled] = useState(false);
  const [biometricAvailable, setBiometricAvailable] = useState(false);
  const [biometricLabel, setBiometricLabel] = useState("biometrics");
  const [biometricFailureCount, setBiometricFailureCount] = useState(0);
  const [shouldOfferBiometrics, setShouldOfferBiometrics] = useState(false);
  const accessTokenRef = useRef<string | null>(null);
  const sessionRef = useRef<StoredSession | null>(null);
  const rememberSessionRef = useRef(false);
  const biometricEnabledRef = useRef(false);
  const refreshPromiseRef = useRef<Promise<string | null> | null>(null);
  const logoutPromiseRef = useRef<Promise<void> | null>(null);

  const clearSessionStorage = useCallback(async () => {
    try {
      await SecureStore.deleteItemAsync(SESSION_KEY);
    } catch {
      // Ignore secure store cleanup failures during logout.
    }
  }, []);

  const persistSession = useCallback(async (nextSession: StoredSession | null) => {
    if (!nextSession || !rememberSessionRef.current) {
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
        refreshPromiseRef.current = null;

        const hadSession = Boolean(accessTokenRef.current);
        const tokenBeforeClear = accessTokenRef.current;
        accessTokenRef.current = null;
        sessionRef.current = null;
        rememberSessionRef.current = false;
        biometricEnabledRef.current = false;
        setSession(null);
        setIsAppLocked(false);
        setBiometricEnabled(false);
        setBiometricFailureCount(0);
        setShouldOfferBiometrics(false);
        setApiTokenResolver(() => accessTokenRef.current);
        await clearSessionStorage();
        await clearNativeGoogleSignInState();
        clearPendingAuthFlows();
        await queryClient.cancelQueries();
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
      };

      logoutPromiseRef.current = runLogout().finally(() => {
        setIsAuthTransitioning(false);
        logoutPromiseRef.current = null;
      });

      await logoutPromiseRef.current;
    },
    [clearSessionStorage]
  );

  const notifyUserInteraction = useCallback(() => {
    // Device-bound sessions are persistent until manual logout/revocation.
    // Keep this method as a no-op for UI hooks that still call it.
  }, []);

  const applyAuthTokenResponse = useCallback(
    async (response: AuthTokenResponse, rememberMe = true) => {
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
      rememberSessionRef.current = rememberMe;
      setSession(nextSession);
      setApiTokenResolver(() => accessTokenRef.current);
      setSessionMessage(null);
      await persistSession(nextSession);

      const [availability, preference] = await Promise.all([
        getBiometricAvailability(),
        readBiometricPreference(response.user.id)
      ]);
      const isEnabled = availability.available && preference?.decision === "enabled";
      biometricEnabledRef.current = isEnabled;
      setBiometricAvailable(availability.available);
      setBiometricLabel(availability.label);
      setBiometricEnabled(isEnabled);
      setIsAppLocked(false);
      setBiometricFailureCount(0);
      setShouldOfferBiometrics(
        rememberMe && availability.available && preference === null
      );
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
        await applyAuthTokenResponse(refreshed, rememberSessionRef.current);
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

  const unlockWithBiometrics = useCallback(async () => {
    if (!sessionRef.current) {
      return { succeeded: false, message: "Sign in again to continue." };
    }

    const availability = await getBiometricAvailability();
    setBiometricAvailable(availability.available);
    setBiometricLabel(availability.label);
    if (!availability.available) {
      return {
        succeeded: false,
        message: "Biometrics are unavailable. Use your password to sign in again."
      };
    }

    const result = await authenticateWithBiometrics("Unlock NSFinance");
    if (!result.success) {
      if (result.error !== "user_cancel" && result.error !== "system_cancel" && result.error !== "app_cancel") {
        setBiometricFailureCount((current) => current + 1);
      }
      return {
        succeeded: false,
        message: result.error === "user_cancel"
          ? undefined
          : "Your identity could not be verified. Try again or use another method."
      };
    }

    setBiometricFailureCount(0);
    setIsAppLocked(false);
    void refreshSessionUser();
    return { succeeded: true };
  }, [refreshSessionUser]);

  const enableBiometrics = useCallback(async () => {
    const current = sessionRef.current;
    if (!current || !rememberSessionRef.current) {
      return {
        succeeded: false,
        message: "Turn on Remember me before enabling biometric unlock."
      };
    }

    const availability = await getBiometricAvailability();
    setBiometricAvailable(availability.available);
    setBiometricLabel(availability.label);
    if (!availability.available) {
      return {
        succeeded: false,
        message: "Set up biometrics in Android settings, then try again."
      };
    }

    const result = await authenticateWithBiometrics("Use biometrics with NSFinance");
    if (!result.success) {
      return {
        succeeded: false,
        message: result.error === "user_cancel" ? undefined : "Biometric setup was not completed."
      };
    }

    await writeBiometricPreference({ userId: current.user.id, decision: "enabled" });
    biometricEnabledRef.current = true;
    setBiometricEnabled(true);
    setShouldOfferBiometrics(false);
    setBiometricFailureCount(0);
    return { succeeded: true };
  }, []);

  const disableBiometrics = useCallback(async () => {
    const current = sessionRef.current;
    if (current) {
      await writeBiometricPreference({ userId: current.user.id, decision: "declined" });
    }
    biometricEnabledRef.current = false;
    setBiometricEnabled(false);
    setIsAppLocked(false);
    setShouldOfferBiometrics(false);
    setBiometricFailureCount(0);
  }, []);

  const declineBiometrics = useCallback(async () => {
    const current = sessionRef.current;
    if (current) {
      await writeBiometricPreference({ userId: current.user.id, decision: "declined" });
    }
    biometricEnabledRef.current = false;
    setBiometricEnabled(false);
    setShouldOfferBiometrics(false);
  }, []);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState: AppStateStatus) => {
      if (
        nextState !== "active"
        && biometricEnabledRef.current
        && rememberSessionRef.current
        && sessionRef.current
      ) {
        setIsAppLocked(true);
      }
    });

    return () => subscription.remove();
  }, []);

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

        rememberSessionRef.current = true;
        accessTokenRef.current = parsed.accessToken;
        sessionRef.current = parsed;
        setSession(parsed);
        setApiTokenResolver(() => accessTokenRef.current);

        const [availability, preference] = await Promise.all([
          getBiometricAvailability(),
          readBiometricPreference(parsed.user.id)
        ]);
        const preferenceEnabled = preference?.decision === "enabled";
        biometricEnabledRef.current = preferenceEnabled;
        setBiometricAvailable(availability.available);
        setBiometricLabel(availability.label);
        setBiometricEnabled(preferenceEnabled);
        setShouldOfferBiometrics(availability.available && preference === null);

        if (preferenceEnabled) {
          setIsAppLocked(true);
          return;
        }

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
        rememberSessionRef.current = false;
        biometricEnabledRef.current = false;
        setSession(null);
        setIsAppLocked(false);
        setBiometricEnabled(false);
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
      isAuthenticated: Boolean(session) && !isAppLocked,
      isAppLocked,
      biometricEnabled,
      biometricAvailable,
      biometricLabel,
      biometricFailureCount,
      shouldOfferBiometrics,
      session,
      sessionMessage,
      applyAuthTokenResponse,
      refreshSessionUser,
      unlockWithBiometrics,
      enableBiometrics,
      disableBiometrics,
      declineBiometrics,
      logout,
      clearSessionMessage: () => setSessionMessage(null),
      notifyUserInteraction
    }),
    [
      applyAuthTokenResponse,
      biometricAvailable,
      biometricEnabled,
      biometricFailureCount,
      biometricLabel,
      declineBiometrics,
      disableBiometrics,
      enableBiometrics,
      refreshSessionUser,
      isBootstrapping,
      isAuthTransitioning,
      isAppLocked,
      logout,
      notifyUserInteraction,
      session,
      sessionMessage,
      shouldOfferBiometrics,
      unlockWithBiometrics
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
