import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as SecureStore from "expo-secure-store";
import { AppState } from "react-native";
import {
  getCurrentUser,
  logout as logoutApi,
  logoutWithAccessToken,
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
  shouldOfferSessionProtection,
  shouldRememberSession,
  shouldReviewBiometricFallback,
  shouldLockSessionForAppExit
} from "../features/auth/sessionProtectionPolicy";
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
  shouldOfferBiometrics: boolean;
  shouldReviewBiometricAfterFallback: boolean;
  allowAutomaticBiometricPrompt: boolean;
  lockedAccountDisplayName: string | null;
  session: StoredSession | null;
  sessionMessage: string | null;
  applyAuthTokenResponse: (response: AuthTokenResponse, rememberSession?: boolean) => Promise<void>;
  refreshSessionUser: () => Promise<void>;
  unlockWithBiometrics: () => Promise<{ succeeded: boolean; message?: string }>;
  enableBiometrics: () => Promise<{ succeeded: boolean; message?: string }>;
  disableBiometrics: () => Promise<void>;
  declineBiometrics: () => Promise<void>;
  keepBiometricsAfterFallback: () => Promise<void>;
  signInAnotherWay: () => Promise<void>;
  prepareForAppExit: () => void;
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
  const [shouldOfferBiometrics, setShouldOfferBiometrics] = useState(false);
  const [shouldReviewBiometricAfterFallback, setShouldReviewBiometricAfterFallback] = useState(false);
  const [allowAutomaticBiometricPrompt, setAllowAutomaticBiometricPrompt] = useState(true);
  const [lockedAccountDisplayName, setLockedAccountDisplayName] = useState<string | null>(null);
  const accessTokenRef = useRef<string | null>(null);
  const sessionRef = useRef<StoredSession | null>(null);
  const lockedSessionRef = useRef<StoredSession | null>(null);
  const rememberSessionRef = useRef(false);
  const biometricEnabledRef = useRef(false);
  const allowAutomaticBiometricPromptRef = useRef(true);
  const refreshPromiseRef = useRef<Promise<string | null> | null>(null);
  const logoutPromiseRef = useRef<Promise<void> | null>(null);
  const biometricFallbackUserIdRef = useRef<string | null>(null);

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

        const tokenBeforeClear = accessTokenRef.current ?? lockedSessionRef.current?.accessToken ?? null;
        const hadSession = Boolean(tokenBeforeClear);
        accessTokenRef.current = null;
        sessionRef.current = null;
        lockedSessionRef.current = null;
        rememberSessionRef.current = false;
        biometricEnabledRef.current = false;
        allowAutomaticBiometricPromptRef.current = true;
        setSession(null);
        setIsAppLocked(false);
        setLockedAccountDisplayName(null);
        setBiometricEnabled(false);
        setAllowAutomaticBiometricPrompt(true);
        setShouldOfferBiometrics(false);
        setShouldReviewBiometricAfterFallback(false);
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
    async (response: AuthTokenResponse, rememberSession?: boolean) => {
      const nextSession: StoredSession = {
        accessToken: response.accessToken,
        accessTokenExpiresAtUtc: response.accessTokenExpiresAtUtc,
        refreshToken: response.refreshToken,
        refreshTokenExpiresAtUtc: response.refreshTokenExpiresAtUtc,
        sessionId: response.sessionId,
        user: response.user
      };

      const [availability, preference] = await Promise.all([
        getBiometricAvailability(),
        readBiometricPreference(response.user.id)
      ]);
      const preferenceEnabled = preference?.decision === "enabled";
      const shouldRemember = shouldRememberSession({
        explicitDecision: rememberSession,
        biometricPreference: preference
      });

      accessTokenRef.current = response.accessToken;
      sessionRef.current = nextSession;
      rememberSessionRef.current = shouldRemember;
      setSession(nextSession);
      setApiTokenResolver(() => accessTokenRef.current);
      setSessionMessage(null);
      await persistSession(nextSession);

      setBiometricAvailable(availability.available);
      setBiometricLabel(availability.label);
      setBiometricEnabled(preferenceEnabled);
      biometricEnabledRef.current = preferenceEnabled;
      if (!lockedSessionRef.current) {
        setIsAppLocked(false);
        setLockedAccountDisplayName(null);
      }
      setShouldOfferBiometrics(shouldOfferSessionProtection({
        explicitDecision: rememberSession,
        biometricAvailable: availability.available,
        biometricPreference: preference
      }));
      const fallbackUserId = biometricFallbackUserIdRef.current;
      biometricFallbackUserIdRef.current = null;
      setShouldReviewBiometricAfterFallback(shouldReviewBiometricFallback({
        fallbackUserId,
        authenticatedUserId: response.user.id,
        biometricPreference: preference
      }));
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
    const lockedSession = lockedSessionRef.current;
    if (!lockedSession) {
      return { succeeded: false, message: "Sign in again to continue." };
    }

    const availability = await getBiometricAvailability();
    setBiometricAvailable(availability.available);
    setBiometricLabel(availability.label);
    if (!availability.available) {
      return {
        succeeded: false,
        message: "Biometrics are unavailable. Sign in another way to continue."
      };
    }

    const result = await authenticateWithBiometrics({
      promptMessage: "Welcome back!",
      promptDescription: "Use your fingerprint to log back into your account.",
      cancelLabel: "Use another method"
    });
    if (!result.success) {
      const wasCancelled = ["user_cancel", "system_cancel", "app_cancel", "user_fallback"]
        .includes(result.error);
      return {
        succeeded: false,
        message: wasCancelled
          ? undefined
          : "Your identity could not be verified. Try again or use another method."
      };
    }

    rememberSessionRef.current = true;
    accessTokenRef.current = lockedSession.accessToken;
    sessionRef.current = lockedSession;
    setApiTokenResolver(() => accessTokenRef.current);

    const accessExpiry = Date.parse(lockedSession.accessTokenExpiresAtUtc);
    if (Number.isNaN(accessExpiry) || accessExpiry <= Date.now()) {
      const refreshedToken = await refreshAccessToken();
      if (!refreshedToken || !sessionRef.current) {
        return {
          succeeded: false,
          message: "Your remembered session is no longer available. Sign in again to continue."
        };
      }
    } else {
      setSession(lockedSession);
    }

    lockedSessionRef.current = null;
    allowAutomaticBiometricPromptRef.current = true;
    setLockedAccountDisplayName(null);
    setIsAppLocked(false);
    setAllowAutomaticBiometricPrompt(true);
    void refreshSessionUser();
    return { succeeded: true };
  }, [refreshAccessToken, refreshSessionUser]);

  const enableBiometrics = useCallback(async () => {
    const current = sessionRef.current;
    if (!current) {
      return {
        succeeded: false,
        message: "Sign in again before enabling fingerprint unlock."
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

    const result = await authenticateWithBiometrics({
      promptMessage: "Use fingerprint with NSFinance",
      promptDescription: "Confirm your fingerprint to protect this remembered session.",
      cancelLabel: "Not now"
    });
    if (!result.success) {
      return {
        succeeded: false,
        message: result.error === "user_cancel" ? undefined : "Biometric setup was not completed."
      };
    }

    try {
      rememberSessionRef.current = true;
      await persistSession(current);
      await writeBiometricPreference({
        userId: current.user.id,
        decision: "enabled",
        fallbackReviewDismissed: false
      });
      setBiometricEnabled(true);
      biometricEnabledRef.current = true;
      setShouldOfferBiometrics(false);
      return { succeeded: true };
    } catch {
      rememberSessionRef.current = false;
      await clearSessionStorage();
      return {
        succeeded: false,
        message: "Fingerprint unlock could not be saved on this device."
      };
    }
  }, [clearSessionStorage, persistSession]);

  const disableBiometrics = useCallback(async () => {
    const current = sessionRef.current;
    if (current) {
      await writeBiometricPreference({ userId: current.user.id, decision: "declined" });
    }
    rememberSessionRef.current = false;
    await clearSessionStorage();
    setBiometricEnabled(false);
    biometricEnabledRef.current = false;
    setIsAppLocked(false);
    setShouldOfferBiometrics(false);
    setShouldReviewBiometricAfterFallback(false);
  }, [clearSessionStorage]);

  const declineBiometrics = useCallback(async () => {
    const current = sessionRef.current;
    if (current) {
      await writeBiometricPreference({ userId: current.user.id, decision: "declined" });
    }
    rememberSessionRef.current = false;
    await clearSessionStorage();
    setBiometricEnabled(false);
    biometricEnabledRef.current = false;
    setShouldOfferBiometrics(false);
  }, [clearSessionStorage]);

  const keepBiometricsAfterFallback = useCallback(async () => {
    const current = sessionRef.current;
    if (current) {
      await writeBiometricPreference({
        userId: current.user.id,
        decision: "enabled",
        fallbackReviewDismissed: true
      });
    }
    setShouldReviewBiometricAfterFallback(false);
  }, []);

  const signInAnotherWay = useCallback(async () => {
    const lockedUserId = lockedSessionRef.current?.user.id ?? null;
    await logout();
    biometricFallbackUserIdRef.current = lockedUserId;
  }, [logout]);

  const prepareForAppExit = useCallback(() => {
    const current = sessionRef.current;
    if (!current) {
      return;
    }

    void queryClient.cancelQueries();
    queryClient.clear();

    if (shouldLockSessionForAppExit({
      rememberedSession: rememberSessionRef.current,
      biometricEnabled: biometricEnabledRef.current
    })) {
      lockedSessionRef.current = current;
      accessTokenRef.current = null;
      sessionRef.current = null;
      setSession(null);
      setApiTokenResolver(() => null);
      setLockedAccountDisplayName(current.user.displayName || current.user.fullName || null);
      allowAutomaticBiometricPromptRef.current = false;
      setAllowAutomaticBiometricPrompt(false);
      setIsAppLocked(true);
      return;
    }

    const accessToken = current.accessToken;
    accessTokenRef.current = null;
    sessionRef.current = null;
    rememberSessionRef.current = false;
    setSession(null);
    setShouldOfferBiometrics(false);
    setShouldReviewBiometricAfterFallback(false);
    setApiTokenResolver(() => null);
    void clearSessionStorage();
    void logoutWithAccessToken(accessToken).catch(() => undefined);
  }, [clearSessionStorage]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      if (
        nextState === "active"
        && lockedSessionRef.current
        && !allowAutomaticBiometricPromptRef.current
      ) {
        allowAutomaticBiometricPromptRef.current = true;
        setAllowAutomaticBiometricPrompt(true);
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

        if (preference?.decision === "declined") {
          await clearSessionStorage();
          return;
        }

        if (preferenceEnabled) {
          rememberSessionRef.current = true;
          lockedSessionRef.current = parsed;
          setLockedAccountDisplayName(parsed.user.displayName || parsed.user.fullName || null);
          setIsAppLocked(true);
          return;
        }

        rememberSessionRef.current = true;
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
        lockedSessionRef.current = null;
        rememberSessionRef.current = false;
        biometricEnabledRef.current = false;
        setSession(null);
        setIsAppLocked(false);
        setLockedAccountDisplayName(null);
        setBiometricEnabled(false);
        setShouldReviewBiometricAfterFallback(false);
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
      shouldOfferBiometrics,
      shouldReviewBiometricAfterFallback,
      allowAutomaticBiometricPrompt,
      lockedAccountDisplayName,
      session,
      sessionMessage,
      applyAuthTokenResponse,
      refreshSessionUser,
      unlockWithBiometrics,
      enableBiometrics,
      disableBiometrics,
      declineBiometrics,
      keepBiometricsAfterFallback,
      signInAnotherWay,
      prepareForAppExit,
      logout,
      clearSessionMessage: () => setSessionMessage(null),
      notifyUserInteraction
    }),
    [
      applyAuthTokenResponse,
      allowAutomaticBiometricPrompt,
      biometricAvailable,
      biometricEnabled,
      biometricLabel,
      declineBiometrics,
      disableBiometrics,
      enableBiometrics,
      refreshSessionUser,
      isBootstrapping,
      isAuthTransitioning,
      isAppLocked,
      keepBiometricsAfterFallback,
      lockedAccountDisplayName,
      logout,
      notifyUserInteraction,
      prepareForAppExit,
      session,
      sessionMessage,
      shouldReviewBiometricAfterFallback,
      signInAnotherWay,
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
