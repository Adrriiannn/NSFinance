import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ionicons } from "@expo/vector-icons";
import { useNavigation } from "@react-navigation/native";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import {
  Alert, Modal, Pressable, ScrollView, Share, StyleSheet, Switch, Text, View } from "react-native";
import QRCode from "react-native-qrcode-svg";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { AccountProviderBadge } from "../../../src/components/accounts/AccountProviderBadge";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  getSessions,
  logoutAll,
  revokeSession,
  requestAccountDeletionCode,
  requestPasswordChangeCode,
  verifyPasswordChangeCode,
  confirmPasswordChangeWithCode,
  checkPasswordPolicy
} from "../../../src/features/auth/authApi";
import {
  useConnectedBanksQuery,
  useDisconnectBankConnectionMutation,
  useLinkedBankAccountsQuery
} from "../../../src/features/banking/useBanking";
import { resolveConnectedBankIdentity } from "../../../src/features/accounts/providerBranding";
import { ApiClientError, formatUnknownError } from "../../../src/lib/api/errors";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import type {
  BankConnectionStatus,
  BeginTotpEnrollmentResponse,
  LinkedBankAccountDto
} from "../../../src/types/api";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import {
  type PasswordBreachStatus,
  enforcePasswordMaxLength,
  hasNumberOrSymbol,
  isLengthWithinPolicy,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  sanitizePasswordInput
} from "../../../src/features/auth/passwordPolicy";
import {
  useCreateDeletionRequestMutation,
  useMyDeletionRequestsQuery
} from "../../../src/features/support/useSupport";
import {
  useBeginTotpEnrollmentMutation,
  useConfirmTotpEnrollmentMutation,
  useDisableMfaMutation,
  useMfaStatusQuery
} from "../../../src/features/auth/useAuthMutations";
import { useAuthSession } from "../../../src/providers/AuthProvider";

const sessionKey = ["auth", "sessions"] as const;

function formatDateTime(value?: string | null) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "-";
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false
  }).format(date);
}

function isActiveSession(session: { revokedUtc: string | null; expiresUtc: string }) {
  if (session.revokedUtc) {
    return false;
  }

  const expiresAt = new Date(session.expiresUtc).getTime();
  if (Number.isNaN(expiresAt)) {
    return true;
  }

  return expiresAt > Date.now();
}

function isSessionNotFoundError(error: unknown) {
  return error instanceof ApiClientError && (error.code === "session_not_found" || error.status === 404);
}

type BankConnectionStatusTone = "positive" | "warning" | "neutral" | "negative";

function formatBankConnectionStatus(status: BankConnectionStatus): {
  label: string;
  tone: BankConnectionStatusTone;
} {
  switch (status) {
    case "connected_pending_sync":
      return { label: "Connecting", tone: "neutral" };
    case "connected":
      return { label: "Connected", tone: "neutral" };
    case "sync_pending":
      return { label: "Updating", tone: "neutral" };
    case "synced":
      return { label: "Up to date", tone: "positive" };
    case "reauth_required":
      return { label: "Reconnect required", tone: "warning" };
    case "expired":
      return { label: "Consent expired", tone: "warning" };
    case "disconnect_pending":
      return { label: "Disconnecting", tone: "neutral" };
    case "disconnect_failed":
      return { label: "Needs attention", tone: "negative" };
    case "failed":
      return { label: "Needs attention", tone: "negative" };
    case "connection_started":
    case "consent_in_progress":
      return { label: "Authorizing", tone: "neutral" };
    case "revoked":
      return { label: "Disconnected", tone: "neutral" };
    case "not_connected":
      return { label: "Not connected", tone: "neutral" };
    default:
      return { label: status, tone: "neutral" };
  }
}

function buildLinkedAccountSummary(accounts: LinkedBankAccountDto[] | undefined) {
  const summaries = new Map<string, { count: number; names: string[] }>();
  for (const account of accounts ?? []) {
    const current = summaries.get(account.connectionId) ?? { count: 0, names: [] };
    current.count += 1;

    const label = account.displayName.trim();
    if (label.length > 0 && !current.names.includes(label)) {
      current.names.push(label);
    }

    summaries.set(account.connectionId, current);
  }

  return summaries;
}

export default function SecuritySettingsScreen() {
  const router = useRouter();
  const navigation = useNavigation();
  const queryClient = useQueryClient();
  const {
    biometricEnabled,
    biometricAvailable,
    biometricLabel,
    enableBiometrics,
    disableBiometrics
  } = useAuthSession();
  const mfaStatusQuery = useMfaStatusQuery();
  const beginMfaMutation = useBeginTotpEnrollmentMutation();
  const confirmMfaMutation = useConfirmTotpEnrollmentMutation();
  const disableMfaMutation = useDisableMfaMutation();
  const sessionsQuery = useQuery({ queryKey: sessionKey, queryFn: getSessions });
  const connectedBanksQuery = useConnectedBanksQuery();
  const linkedBankAccountsQuery = useLinkedBankAccountsQuery();
  const disconnectMutation = useDisconnectBankConnectionMutation();
  const deletionRequestsQuery = useMyDeletionRequestsQuery();
  const createDeletionMutation = useCreateDeletionRequestMutation();

  const revokeMutation = useMutation({
    mutationFn: (sessionId: string) => revokeSession(sessionId),
    onMutate: async (sessionId: string) => {
      await queryClient.cancelQueries({ queryKey: sessionKey });
      const previousSessions = queryClient.getQueryData(sessionKey);
      queryClient.setQueryData(sessionKey, (current: typeof previousSessions) => {
        if (!Array.isArray(current)) {
          return current;
        }

        return current.filter((session) => session.id !== sessionId);
      });

      return { previousSessions };
    },
    onError: async (error, _sessionId, context) => {
      if (isSessionNotFoundError(error)) {
        showFlashMessage("This session had already ended and was removed.", { tone: "info" });
        await queryClient.invalidateQueries({ queryKey: sessionKey });
        return;
      }

      if (context?.previousSessions) {
        queryClient.setQueryData(sessionKey, context.previousSessions);
      }

      showFlashMessage(formatUnknownError(error), { tone: "error", durationMs: 2800 });
    },
    onSuccess: async () => {
      showFlashMessage("Session terminated.", { tone: "success" });
      await queryClient.invalidateQueries({ queryKey: sessionKey });
    }
  });

  const logoutAllMutation = useMutation({
    mutationFn: () => logoutAll(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: sessionKey });
    }
  });

  const [disconnectingConnectionId, setDisconnectingConnectionId] = useState<string | null>(null);

  const [passwordCodeModalVisible, setPasswordCodeModalVisible] = useState(false);
  const [passwordResetModalVisible, setPasswordResetModalVisible] = useState(false);
  const [passwordCode, setPasswordCode] = useState("");
  const [passwordChallengeId, setPasswordChallengeId] = useState<string | null>(null);
  const [passwordGrantToken, setPasswordGrantToken] = useState<string | null>(null);
  const [passwordCodeError, setPasswordCodeError] = useState<string | null>(null);
  const [newPassword, setNewPassword] = useState("");
  const [confirmNewPassword, setConfirmNewPassword] = useState("");
  const [passwordBreachStatus, setPasswordBreachStatus] = useState<PasswordBreachStatus>("idle");
  const [passwordBreachMessage, setPasswordBreachMessage] = useState<string | null>(null);

  const [deletionNote, setDeletionNote] = useState("");
  const [deletionConfirmationText, setDeletionConfirmationText] = useState("");
  const [deletionCodeModalVisible, setDeletionCodeModalVisible] = useState(false);
  const [deletionCode, setDeletionCode] = useState("");
  const [deletionCodeError, setDeletionCodeError] = useState<string | null>(null);
  const [mfaModalVisible, setMfaModalVisible] = useState(false);
  const [mfaEnrollment, setMfaEnrollment] = useState<BeginTotpEnrollmentResponse | null>(null);
  const [mfaCode, setMfaCode] = useState("");
  const [mfaError, setMfaError] = useState<string | null>(null);
  const [mfaRecoveryCodes, setMfaRecoveryCodes] = useState<string[]>([]);
  const [mfaDisableMode, setMfaDisableMode] = useState(false);
  const [mfaMethod, setMfaMethod] = useState<"totp" | "recovery_code">("totp");

  const activeSessions = useMemo(
    () => (sessionsQuery.data ?? []).filter(isActiveSession),
    [sessionsQuery.data]
  );

  const currentSession = useMemo(
    () => activeSessions.find((session) => session.isCurrentSession) ?? null,
    [activeSessions]
  );

  const otherSessions = useMemo(
    () => activeSessions.filter((session) => !session.isCurrentSession),
    [activeSessions]
  );

  const hasPasswordMismatch =
    confirmNewPassword.length > 0 && newPassword !== confirmNewPassword;
  const hasStartedNewPassword = newPassword.length > 0;
  const hasPasswordLengthIssue = hasStartedNewPassword && !isLengthWithinPolicy(newPassword);
  const hasPasswordNumberSymbolIssue = hasStartedNewPassword && !hasNumberOrSymbol(newPassword);
  const hasPasswordReachedMaximum = hasStartedNewPassword && newPassword.length >= PASSWORD_MAX_LENGTH;

  useEffect(() => {
    if (!passwordResetModalVisible) {
      return;
    }

    if (!newPassword || !isLengthWithinPolicy(newPassword)) {
      setPasswordBreachStatus("idle");
      setPasswordBreachMessage(null);
      return;
    }

    let cancelled = false;
    const timer = setTimeout(async () => {
      setPasswordBreachStatus("checking");
      setPasswordBreachMessage(null);

      try {
        const response = await checkPasswordPolicy({ password: newPassword });
        if (cancelled) {
          return;
        }

        if (response.breachStatus === "compromised") {
          setPasswordBreachStatus("compromised");
          setPasswordBreachMessage("This password has appeared in known data breaches.");
          return;
        }

        if (response.breachStatus === "unavailable") {
          setPasswordBreachStatus("unavailable");
          setPasswordBreachMessage("Could not verify compromised-password status right now.");
          return;
        }

        setPasswordBreachStatus("safe");
        setPasswordBreachMessage(null);
      } catch {
        if (!cancelled) {
          setPasswordBreachStatus("unavailable");
          setPasswordBreachMessage("Could not verify compromised-password status right now.");
        }
      }
    }, 550);

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [newPassword, passwordResetModalVisible]);

  const updateBiometricSetting = async (enabled: boolean) => {
    if (!enabled) {
      await disableBiometrics();
      showFlashMessage("Fingerprint unlock and remembered sign-in turned off.", { tone: "info" });
      return;
    }

    const result = await enableBiometrics();
    if (result.succeeded) {
      showFlashMessage("Fingerprint unlock and remembered sign-in are ready.", { tone: "success" });
    } else if (result.message) {
      showFlashMessage(result.message, { tone: "error", durationMs: 3200 });
    }
  };

  const startPasswordChangeFlow = async () => {
    setPasswordCode("");
    setPasswordCodeError(null);
    try {
      const delivery = await requestPasswordChangeCode();
      setPasswordChallengeId(delivery.challengeId);
      setPasswordCodeModalVisible(true);
    } catch (error) {
      showFlashMessage(error instanceof Error ? error.message : "Could not request password code.", { tone: "error", durationMs: 3200 });
    }
  };

  const verifyCodeThenOpenPasswordModal = async () => {
    if (!passwordChallengeId) {
      setPasswordCodeError("Request a new code and try again.");
      return;
    }

    setPasswordCodeError(null);
    try {
      const grant = await verifyPasswordChangeCode({
        challengeId: passwordChallengeId,
        code: passwordCode.trim()
      });
      setPasswordGrantToken(grant.recoveryToken);
      setPasswordCodeModalVisible(false);
      setNewPassword("");
      setConfirmNewPassword("");
      setPasswordBreachStatus("idle");
      setPasswordBreachMessage(null);
      setPasswordResetModalVisible(true);
    } catch {
      setPasswordCode("");
      setPasswordCodeError("The code is wrong.");
    }
  };

  const completePasswordChange = async () => {
    if (
      hasPasswordMismatch
      || hasPasswordLengthIssue
      || hasPasswordNumberSymbolIssue
      || passwordBreachStatus !== "safe"
      || !passwordChallengeId
      || !passwordGrantToken)
    {
      return;
    }

    try {
      await confirmPasswordChangeWithCode({
        challengeId: passwordChallengeId,
        grantToken: passwordGrantToken,
        newPassword
      });

      setPasswordResetModalVisible(false);
      setNewPassword("");
      setConfirmNewPassword("");
      setPasswordChallengeId(null);
      setPasswordGrantToken(null);
      showFlashMessage("Password updated successfully.", { tone: "success" });
    } catch (error) {
      showFlashMessage(error instanceof Error ? error.message : "Could not update password.", { tone: "error", durationMs: 3200 });
    }
  };

  const openMfaFlow = async () => {
    setMfaCode("");
    setMfaError(null);
    setMfaRecoveryCodes([]);
    setMfaMethod("totp");

    if (mfaStatusQuery.data?.enabled) {
      setMfaDisableMode(true);
      setMfaEnrollment(null);
      setMfaModalVisible(true);
      return;
    }

    setMfaDisableMode(false);
    try {
      const enrollment = await beginMfaMutation.mutateAsync();
      setMfaEnrollment(enrollment);
      setMfaModalVisible(true);
    } catch (error) {
      showFlashMessage(formatUnknownError(error), { tone: "error", durationMs: 3200 });
    }
  };

  const submitMfaCode = async () => {
    if (!mfaCode.trim()) {
      setMfaError(mfaMethod === "totp" ? "Enter the six-digit code." : "Enter a recovery code.");
      return;
    }

    setMfaError(null);
    try {
      if (mfaDisableMode) {
        await disableMfaMutation.mutateAsync({ code: mfaCode.trim(), method: mfaMethod });
        setMfaModalVisible(false);
        setMfaCode("");
        showFlashMessage("Authenticator verification turned off.", { tone: "success" });
        return;
      }

      if (!mfaEnrollment) {
        setMfaError("Start authenticator setup again.");
        return;
      }

      const result = await confirmMfaMutation.mutateAsync({
        authenticatorId: mfaEnrollment.authenticatorId,
        code: mfaCode.trim()
      });
      setMfaRecoveryCodes(result.recoveryCodes);
      setMfaCode("");
      showFlashMessage("Authenticator verification is ready.", { tone: "success" });
    } catch (error) {
      setMfaCode("");
      setMfaError(formatUnknownError(error));
    }
  };

  const shareRecoveryCodes = async () => {
    if (mfaRecoveryCodes.length === 0) {
      return;
    }

    await Share.share({
      title: "NSFinance recovery codes",
      message: `NSFinance recovery codes\n\n${mfaRecoveryCodes.join("\n")}\n\nEach code works once.`
    });
  };

  const requestDeletionCode = async () => {
    if (deletionConfirmationText.trim().toUpperCase() !== "DELETE") {
      Alert.alert("Confirmation required", "Type DELETE to continue with account deletion.");
      return;
    }

    try {
      await requestAccountDeletionCode();
      setDeletionCode("");
      setDeletionCodeError(null);
      setDeletionCodeModalVisible(true);
    } catch (error) {
      showFlashMessage(error instanceof Error ? error.message : "Could not request deletion code.", { tone: "error", durationMs: 3200 });
    }
  };

  const submitDeletion = async () => {
    setDeletionCodeError(null);
    try {
      await createDeletionMutation.mutateAsync({
        verificationCode: deletionCode.trim(),
        notes: deletionNote.trim() || "User requested account deletion from Security settings."
      });
      setDeletionCodeModalVisible(false);
      setDeletionCode("");
      setDeletionNote("");
      setDeletionConfirmationText("");
      showFlashMessage("Deletion request submitted.", { tone: "success" });
    } catch {
      setDeletionCode("");
      setDeletionCodeError("The code is wrong.");
    }
  };

  const activeBankConnections = connectedBanksQuery.data?.activeConnections ?? [];
  const attentionBankConnections = connectedBanksQuery.data?.attentionConnections ?? [];
  const linkedAccountSummaryByConnection = useMemo(
    () => buildLinkedAccountSummary(linkedBankAccountsQuery.data),
    [linkedBankAccountsQuery.data]
  );

  const handleDisconnectBank = async (connectionId: string) => {
    showFlashMessage("Disconnection in progress.\nRemoving all the account data.", {
      tone: "info",
      durationMs: 3000
    });

    setDisconnectingConnectionId(connectionId);
    try {
      await disconnectMutation.mutateAsync(connectionId);
      showFlashMessage("Disconnected successfully.", {
        tone: "success"
      });
    } catch (error) {
      showFlashMessage(formatUnknownError(error), { tone: "error", durationMs: 2800 });
    } finally {
      setDisconnectingConnectionId(null);
    }
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <HeaderShell
        preset="secondaryDetail"
        title="Security"
        leadingAction={(
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Go back"
            onPress={() => {
              const state = navigation.getState?.();
              const routeCount = state?.routes?.length ?? 0;
              const previousRoute = routeCount > 1 ? state?.routes?.[routeCount - 2] : undefined;
              const previousName = typeof previousRoute?.name === "string"
                ? previousRoute.name
                : "";

              if (previousName.includes("connect-bank")) {
                router.replace("/(tabs)/accounts" as never);
                return;
              }

              if (navigation.canGoBack()) {
                navigation.goBack();
                return;
              }

              router.replace("/(tabs)/accounts" as never);
            }}
            style={({ pressed }) => [styles.backButton, pressed ? styles.backButtonPressed : null]}
          >
            <Ionicons name="arrow-back" size={20} color={palette.textPrimary} />
          </Pressable>
        )}
      />

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={styles.scrollContent}
      >
        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Login & authentication</Text>
          <Text style={styles.metaLine}>
            Authenticator: {mfaStatusQuery.data?.enabled ? "On" : "Off"}
            {mfaStatusQuery.data?.enabled
              ? ` | ${mfaStatusQuery.data.recoveryCodesRemaining} recovery codes left`
              : ""}
          </Text>

          <SecondaryButton
            label={mfaStatusQuery.data?.enabled ? "Turn off authenticator" : "Set up authenticator"}
            onPress={() => void openMfaFlow()}
            disabled={mfaStatusQuery.isLoading || beginMfaMutation.isPending}
          />

          <View style={styles.toggleRow}>
            <View style={styles.toggleCopy}>
              <Text style={styles.toggleLabel}>
                {biometricLabel === "fingerprint" ? "Fingerprint unlock" : "Biometric unlock"}
              </Text>
              <Text style={styles.metaLine}>
                {biometricAvailable
                  ? "Keeps this phone signed in and protects the next app launch."
                  : "Set up biometrics in Android settings."}
              </Text>
            </View>
            <Switch
              value={biometricEnabled}
              onValueChange={(value) => void updateBiometricSetting(value)}
              disabled={!biometricAvailable && !biometricEnabled}
              thumbColor="#FFFFFF"
              trackColor={{ false: "rgba(120,120,120,0.45)", true: "rgba(242,140,40,0.8)" }}
            />
          </View>

          <PrimaryButton
            label="Change your password"
            onPress={() => {
              void startPasswordChangeFlow();
            }}
          />
        </GlassCard>

        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Sessions & devices</Text>

          {sessionsQuery.isError ? (
            <ErrorState
              title="Could not load sessions"
              message={sessionsQuery.error.message}
              onRetry={() => {
                void sessionsQuery.refetch();
              }}
            />
          ) : (
            <>
              <Text style={styles.subSectionTitle}>Current session</Text>
              {currentSession ? (
                <View style={styles.sessionRow}>
                  <Text style={styles.sessionTitle}>{currentSession.deviceLabel}</Text>
                  <Text style={styles.metaLine}>
                    {currentSession.platform ?? "unknown"} | last seen {formatDateTime(currentSession.lastSeenUtc)}
                  </Text>
                </View>
              ) : (
                <Text style={styles.metaLine}>No active session found.</Text>
              )}

              <Text style={styles.subSectionTitle}>Other sessions</Text>
              {otherSessions.length === 0 ? (
                <Text style={styles.metaLine}>No other active sessions.</Text>
              ) : (
                otherSessions.map((session) => (
                  <View key={session.id} style={styles.sessionRow}>
                    <Text style={styles.sessionTitle}>{session.deviceLabel}</Text>
                    <Text style={styles.metaLine}>
                      {session.platform ?? "unknown"} | last seen {formatDateTime(session.lastSeenUtc)}
                    </Text>
                    <SecondaryButton
                      label="Terminate"
                      onPress={() => {
                        void revokeMutation.mutateAsync(session.id);
                      }}
                      disabled={revokeMutation.isPending}
                    />
                  </View>
                ))
              )}

              <PrimaryButton
                label="Terminate all other sessions"
                onPress={() => {
                  Alert.alert(
                    "Terminate other sessions",
                    "This keeps only your current device signed in.",
                    [
                      { text: "Cancel", style: "cancel" },
                      {
                        text: "Terminate",
                        style: "destructive",
                        onPress: () => {
                          void logoutAllMutation.mutateAsync();
                        }
                      }
                    ]
                  );
                }}
                isLoading={logoutAllMutation.isPending}
                disabled={otherSessions.length === 0}
              />
            </>
          )}
        </GlassCard>

        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Connected banks</Text>
          {connectedBanksQuery.isError ? (
            <Text style={styles.errorText}>{formatUnknownError(connectedBanksQuery.error)}</Text>
          ) : activeBankConnections.length === 0 ? (
            <Text style={styles.metaLine}>No connected banks right now.</Text>
          ) : (
            activeBankConnections.map((connection) => {
              const identity = resolveConnectedBankIdentity({
                providerId: connection.providerId,
                providerDisplayName: connection.providerDisplayName ?? connection.provider,
                providerIconUrl: connection.providerIconUrl,
                providerLogoUrl: connection.providerLogoUrl
              });
              const linkedSummary = linkedAccountSummaryByConnection.get(connection.id);
              const linkedAccountCount = Math.max(0, connection.linkedAccountCount ?? linkedSummary?.count ?? 0);
              const accountCountLine = linkedAccountCount > 0
                ? `${linkedAccountCount} linked account${linkedAccountCount === 1 ? "" : "s"}`
                : "Waiting for accounts";
              const previewNames = linkedSummary?.names.slice(0, 2) ?? [];
              const accountPreview = previewNames.length === 0
                ? null
                : linkedAccountCount > previewNames.length
                  ? `${previewNames.join(", ")} +${linkedAccountCount - previewNames.length} more`
                  : previewNames.join(", ");
              const status = formatBankConnectionStatus(connection.status);
              const statusToneStyle = status.tone === "positive"
                ? styles.statusPillPositive
                : status.tone === "warning"
                  ? styles.statusPillWarning
                  : status.tone === "negative"
                    ? styles.statusPillNegative
                    : styles.statusPillNeutral;
              const statusTextToneStyle = status.tone === "positive"
                ? styles.statusPillTextPositive
                : status.tone === "warning"
                  ? styles.statusPillTextWarning
                  : status.tone === "negative"
                    ? styles.statusPillTextNegative
                    : styles.statusPillTextNeutral;
              const lastUpdatedUtc =
                connection.lastSuccessfulSyncUtc ?? connection.lastSyncAttemptedUtc ?? connection.updatedUtc;

              return (
                <View key={connection.id} style={styles.bankRow}>
                  <View style={styles.bankHeaderRow}>
                    <Text style={styles.bankTitle}>{identity.title}</Text>
                    <AccountProviderBadge
                      account={{
                        providerId: connection.providerId,
                        providerDisplayName: connection.providerDisplayName,
                        providerIconUrl: connection.providerIconUrl,
                        providerLogoUrl: connection.providerLogoUrl
                      }}
                      compact
                    />
                  </View>

                  <Text style={styles.metaLine}>Connected on: {formatDateTime(connection.createdUtc)}</Text>
                  <Text style={styles.metaLine}>Last updated: {formatDateTime(lastUpdatedUtc)}</Text>
                  <Text style={styles.metaLine}>Accounts linked: {accountCountLine}</Text>
                  {accountPreview ? (
                    <Text style={styles.metaLine}>Accounts: {accountPreview}</Text>
                  ) : null}
                  {connection.connectedFullName ? (
                    <Text style={styles.metaLine}>Connected as: {connection.connectedFullName}</Text>
                  ) : null}

                  <View style={styles.statusRow}>
                    <View style={[styles.statusPill, statusToneStyle]}>
                      <Text style={[styles.statusPillText, statusTextToneStyle]}>{status.label}</Text>
                    </View>
                  </View>

                  <SecondaryButton
                    label={
                      disconnectingConnectionId === connection.id || connection.status === "disconnect_pending"
                        ? "Disconnecting..."
                        : "Disconnect bank"
                    }
                    onPress={() => {
                      if (connection.status === "disconnect_pending") {
                        return;
                      }

                      Alert.alert(
                        "Disconnect bank",
                        "This disconnects the bank and removes imported accounts, transactions, balances, and summaries from the app.",
                        [
                          { text: "Cancel", style: "cancel" },
                          {
                            text: "Disconnect",
                            style: "destructive",
                            onPress: () => {
                              void handleDisconnectBank(connection.id);
                            }
                          }
                        ]
                      );
                    }}
                    disabled={disconnectMutation.isPending || connection.status === "disconnect_pending"}
                  />
                </View>
              );
            })
          )}

          {attentionBankConnections.length > 0 ? (
            <>
              <Text style={styles.subSectionTitle}>Needs attention</Text>
              <Text style={styles.hintText}>
                Reconnect these banks to resume updates.
              </Text>
              {attentionBankConnections.map((connection) => {
                const identity = resolveConnectedBankIdentity({
                  providerId: connection.providerId,
                  providerDisplayName: connection.providerDisplayName ?? connection.provider,
                  providerIconUrl: connection.providerIconUrl,
                  providerLogoUrl: connection.providerLogoUrl
                });
                const linkedSummary = linkedAccountSummaryByConnection.get(connection.id);
                const linkedAccountCount = Math.max(0, connection.linkedAccountCount ?? linkedSummary?.count ?? 0);
                const accountCountLine = linkedAccountCount > 0
                  ? `${linkedAccountCount} linked account${linkedAccountCount === 1 ? "" : "s"}`
                  : "Waiting for accounts";
                const previewNames = linkedSummary?.names.slice(0, 2) ?? [];
                const accountPreview = previewNames.length === 0
                  ? null
                  : linkedAccountCount > previewNames.length
                    ? `${previewNames.join(", ")} +${linkedAccountCount - previewNames.length} more`
                    : previewNames.join(", ");
                const status = formatBankConnectionStatus(connection.status);
                const statusToneStyle = status.tone === "positive"
                  ? styles.statusPillPositive
                  : status.tone === "warning"
                    ? styles.statusPillWarning
                    : status.tone === "negative"
                      ? styles.statusPillNegative
                      : styles.statusPillNeutral;
                const statusTextToneStyle = status.tone === "positive"
                  ? styles.statusPillTextPositive
                  : status.tone === "warning"
                    ? styles.statusPillTextWarning
                    : status.tone === "negative"
                      ? styles.statusPillTextNegative
                      : styles.statusPillTextNeutral;
                const lastUpdatedUtc =
                  connection.lastSuccessfulSyncUtc ?? connection.lastSyncAttemptedUtc ?? connection.updatedUtc;

                return (
                  <View key={connection.id} style={styles.bankRow}>
                    <View style={styles.bankHeaderRow}>
                      <Text style={styles.bankTitle}>{identity.title}</Text>
                      <AccountProviderBadge
                        account={{
                          providerId: connection.providerId,
                          providerDisplayName: connection.providerDisplayName,
                          providerIconUrl: connection.providerIconUrl,
                          providerLogoUrl: connection.providerLogoUrl
                        }}
                        compact
                      />
                    </View>

                    <Text style={styles.metaLine}>Connected on: {formatDateTime(connection.createdUtc)}</Text>
                    <Text style={styles.metaLine}>Last updated: {formatDateTime(lastUpdatedUtc)}</Text>
                    <Text style={styles.metaLine}>Accounts linked: {accountCountLine}</Text>
                    {accountPreview ? (
                      <Text style={styles.metaLine}>Accounts: {accountPreview}</Text>
                    ) : null}
                    {connection.connectedFullName ? (
                      <Text style={styles.metaLine}>Connected as: {connection.connectedFullName}</Text>
                    ) : null}

                    <View style={styles.statusRow}>
                      <View style={[styles.statusPill, statusToneStyle]}>
                        <Text style={[styles.statusPillText, statusTextToneStyle]}>{status.label}</Text>
                      </View>
                    </View>

                    <SecondaryButton
                      label={
                        disconnectingConnectionId === connection.id || connection.status === "disconnect_pending"
                          ? "Disconnecting..."
                          : connection.status === "disconnect_failed"
                            ? "Retry remove bank"
                            : "Remove bank"
                      }
                      onPress={() => {
                        if (connection.status === "disconnect_pending") {
                          return;
                        }

                        Alert.alert(
                          connection.status === "disconnect_failed" ? "Retry remove bank" : "Remove bank",
                          "This removes the stale bank link and all imported data that came from it.",
                          [
                            { text: "Cancel", style: "cancel" },
                            {
                              text: connection.status === "disconnect_failed" ? "Retry remove" : "Remove",
                              style: "destructive",
                              onPress: () => {
                                void handleDisconnectBank(connection.id);
                              }
                            }
                          ]
                        );
                      }}
                      disabled={disconnectMutation.isPending || connection.status === "disconnect_pending"}
                    />
                  </View>
                );
              })}
            </>
          ) : null}
        </GlassCard>

        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Delete account</Text>
          <Text style={styles.warningText}>
            Deletion is a serious action. Linked banks are disconnected and active data is removed from normal use.
          </Text>
          <TextField
            label="Reason / note (optional)"
            value={deletionNote}
            onChangeText={setDeletionNote}
            multiline
            numberOfLines={3}
            textAlignVertical="top"
          />
          <TextField
            label="Type DELETE to continue"
            value={deletionConfirmationText}
            onChangeText={setDeletionConfirmationText}
            autoCapitalize="characters"
          />
          <PrimaryButton
            label="Delete my account"
            onPress={() => {
              void requestDeletionCode();
            }}
            disabled={deletionConfirmationText.trim().toUpperCase() !== "DELETE"}
          />
          {(deletionRequestsQuery.data ?? []).slice(0, 3).map((request) => (
            <Text key={request.id} style={styles.metaLine}>
              {request.status} | requested {formatDateTime(request.requestedUtc)}
            </Text>
          ))}
        </GlassCard>

        {logoutAllMutation.isError ? (
          <Text style={styles.errorText}>{formatUnknownError(logoutAllMutation.error)}</Text>
        ) : null}
      </ScrollView>

      <Modal
        visible={mfaModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setMfaModalVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setMfaModalVisible(false)}>
          <Pressable style={styles.modalCard} onPress={() => undefined}>
            {mfaRecoveryCodes.length > 0 ? (
              <>
                <Text style={styles.modalTitle}>Save your recovery codes</Text>
                <Text style={styles.metaLine}>
                  Keep these somewhere private. Each code works once if you lose your authenticator.
                </Text>
                <View style={styles.recoveryCodes}>
                  {mfaRecoveryCodes.map((recoveryCode) => (
                    <Text key={recoveryCode} selectable style={styles.recoveryCode}>
                      {recoveryCode}
                    </Text>
                  ))}
                </View>
                <SecondaryButton label="Share securely" onPress={() => void shareRecoveryCodes()} />
                <PrimaryButton
                  label="I've saved them"
                  onPress={() => {
                    setMfaModalVisible(false);
                    setMfaRecoveryCodes([]);
                    setMfaEnrollment(null);
                  }}
                />
              </>
            ) : mfaDisableMode ? (
              <>
                <Text style={styles.modalTitle}>Turn off authenticator?</Text>
                <Text style={styles.metaLine}>
                  Confirm with your authenticator or one unused recovery code.
                </Text>
                <TextField
                  label={mfaMethod === "totp" ? "Authenticator code" : "Recovery code"}
                  value={mfaCode}
                  onChangeText={(value) => {
                    setMfaCode(mfaMethod === "totp" ? value.replace(/\D/g, "").slice(0, 6) : value.toUpperCase());
                    setMfaError(null);
                  }}
                  keyboardType={mfaMethod === "totp" ? "number-pad" : "default"}
                  autoCapitalize={mfaMethod === "totp" ? "none" : "characters"}
                  error={mfaError ?? undefined}
                />
                <SecondaryButton
                  label={mfaMethod === "totp" ? "Use a recovery code" : "Use authenticator code"}
                  onPress={() => {
                    setMfaMethod((current) => (current === "totp" ? "recovery_code" : "totp"));
                    setMfaCode("");
                    setMfaError(null);
                  }}
                />
                <PrimaryButton
                  label="Turn off authenticator"
                  onPress={() => void submitMfaCode()}
                  disabled={!mfaCode.trim()}
                  isLoading={disableMfaMutation.isPending}
                />
              </>
            ) : mfaEnrollment ? (
              <>
                <Text style={styles.modalTitle}>Set up authenticator</Text>
                <Text style={styles.metaLine}>
                  Scan this QR code in Microsoft Authenticator, Google Authenticator, or another TOTP app.
                </Text>
                <View style={styles.qrWrap}>
                  <QRCode
                    value={mfaEnrollment.otpAuthUri}
                    size={190}
                    color="#111111"
                    backgroundColor="#FFFFFF"
                  />
                </View>
                <Text style={styles.metaLine}>Manual setup key</Text>
                <Text selectable style={styles.manualSecret}>{mfaEnrollment.secret}</Text>
                <TextField
                  label="Six-digit code"
                  value={mfaCode}
                  onChangeText={(value) => {
                    setMfaCode(value.replace(/\D/g, "").slice(0, 6));
                    setMfaError(null);
                  }}
                  keyboardType="number-pad"
                  autoComplete="one-time-code"
                  error={mfaError ?? undefined}
                />
                <PrimaryButton
                  label="Confirm authenticator"
                  onPress={() => void submitMfaCode()}
                  disabled={mfaCode.length !== 6}
                  isLoading={confirmMfaMutation.isPending}
                />
              </>
            ) : null}
          </Pressable>
        </Pressable>
      </Modal>

      <Modal
        visible={passwordCodeModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setPasswordCodeModalVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setPasswordCodeModalVisible(false)}>
          <Pressable style={styles.modalCard} onPress={() => undefined}>
            <Text style={styles.modalTitle}>Enter verification code</Text>
            <TextField
              label="Email code"
              value={passwordCode}
              onChangeText={setPasswordCode}
              autoCapitalize="none"
              autoCorrect={false}
            />
            {passwordCodeError ? <Text style={styles.errorText}>{passwordCodeError}</Text> : null}
            <PrimaryButton
              label="Verify code"
              onPress={() => {
                void verifyCodeThenOpenPasswordModal();
              }}
              disabled={!passwordCode.trim()}
            />
          </Pressable>
        </Pressable>
      </Modal>

      <Modal
        visible={passwordResetModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setPasswordResetModalVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setPasswordResetModalVisible(false)}>
          <Pressable style={styles.modalCard} onPress={() => undefined}>
            <Text style={styles.modalTitle}>Set a new password</Text>
            <TextField
              label="New password"
              value={newPassword}
              onChangeText={(value) => {
                const sanitized = sanitizePasswordInput(value);
                setNewPassword(enforcePasswordMaxLength(sanitized));
              }}
              maxLength={PASSWORD_MAX_LENGTH}
              secureTextEntry
            />
            {hasPasswordLengthIssue ? (
              <Text style={styles.errorText}>
                Use {PASSWORD_MIN_LENGTH} to {PASSWORD_MAX_LENGTH} characters.
              </Text>
            ) : null}
            {hasPasswordNumberSymbolIssue ? (
              <Text style={styles.errorText}>Add a number or symbol.</Text>
            ) : null}
            {hasPasswordReachedMaximum ? (
              <Text style={styles.errorText}>
                You&apos;ve reached the maximum password length of {PASSWORD_MAX_LENGTH} characters.
              </Text>
            ) : null}
            {passwordBreachMessage ? <Text style={styles.errorText}>{passwordBreachMessage}</Text> : null}
            <TextField
              label="Confirm new password"
              value={confirmNewPassword}
              onChangeText={(value) => {
                const sanitized = sanitizePasswordInput(value);
                setConfirmNewPassword(enforcePasswordMaxLength(sanitized));
              }}
              maxLength={PASSWORD_MAX_LENGTH}
              secureTextEntry
              error={hasPasswordMismatch ? "Passwords do not match." : undefined}
            />
            <PrimaryButton
              label="Update password"
              onPress={() => {
                void completePasswordChange();
              }}
              disabled={
                !newPassword.trim()
                || !confirmNewPassword.trim()
                || hasPasswordMismatch
                || hasPasswordLengthIssue
                || hasPasswordNumberSymbolIssue
                || passwordBreachStatus !== "safe"
              }
            />
          </Pressable>
        </Pressable>
      </Modal>

      <Modal
        visible={deletionCodeModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setDeletionCodeModalVisible(false)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setDeletionCodeModalVisible(false)}>
          <Pressable style={styles.modalCard} onPress={() => undefined}>
            <Text style={styles.modalTitle}>Enter deletion verification code</Text>
            <TextField
              label="Email code"
              value={deletionCode}
              onChangeText={setDeletionCode}
              autoCapitalize="none"
              autoCorrect={false}
            />
            {deletionCodeError ? <Text style={styles.errorText}>{deletionCodeError}</Text> : null}
            <PrimaryButton
              label="Confirm deletion request"
              onPress={() => {
                void submitDeletion();
              }}
              isLoading={createDeletionMutation.isPending}
              disabled={!deletionCode.trim()}
            />
          </Pressable>
        </Pressable>
      </Modal>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    paddingTop: 0
  },
  backButton: {
    width: 40,
    height: 40,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  backButtonPressed: {
    opacity: 0.82
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: spacing[16]
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  headerSpacer: {
    width: 42
  },
  scrollContent: {
    gap: spacing[12],
    paddingTop: spacing[10],
    paddingBottom: spacing[12]
  },
  sectionCard: {
    gap: spacing[12]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  subSectionTitle: {
    color: palette.primaryGlow,
    ...typography.caption,
    marginTop: spacing[4]
  },
  sessionRow: {
    gap: spacing[8],
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: surfaces.field,
    padding: spacing[12]
  },
  bankRow: {
    gap: spacing[8],
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: surfaces.field,
    padding: spacing[12]
  },
  bankHeaderRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[10]
  },
  bankTitle: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "700"
  },
  sessionTitle: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  statusRow: {
    marginTop: spacing[2],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-start",
    alignSelf: "flex-start"
  },
  statusPill: {
    minHeight: 24,
    borderRadius: 999,
    borderWidth: 1,
    paddingHorizontal: spacing[10],
    alignItems: "center",
    justifyContent: "center"
  },
  statusPillNeutral: {
    borderColor: palette.border,
    backgroundColor: "rgba(255,255,255,0.04)"
  },
  statusPillPositive: {
    borderColor: "rgba(52, 211, 153, 0.45)",
    backgroundColor: "rgba(34, 197, 94, 0.16)"
  },
  statusPillWarning: {
    borderColor: "rgba(251, 191, 36, 0.42)",
    backgroundColor: "rgba(245, 158, 11, 0.16)"
  },
  statusPillNegative: {
    borderColor: "rgba(248, 113, 113, 0.45)",
    backgroundColor: "rgba(239, 68, 68, 0.18)"
  },
  statusPillText: {
    ...typography.caption,
    fontWeight: "600"
  },
  statusPillTextNeutral: {
    color: palette.textSecondary
  },
  statusPillTextPositive: {
    color: "#86EFAC"
  },
  statusPillTextWarning: {
    color: "#FCD34D"
  },
  statusPillTextNegative: {
    color: "#FCA5A5"
  },
  metaLine: {
    color: palette.textSecondary,
    ...typography.caption
  },
  hintText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  warningText: {
    color: palette.caution,
    ...typography.caption
  },
  successText: {
    color: palette.success,
    ...typography.caption
  },
  errorText: {
    color: palette.negative,
    ...typography.caption
  },
  toggleRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    minHeight: 44,
    paddingHorizontal: spacing[12],
    backgroundColor: surfaces.field
  },
  toggleCopy: {
    flex: 1,
    minWidth: 0,
    gap: spacing[2],
    paddingVertical: spacing[10],
    paddingRight: spacing[12]
  },
  toggleLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: palette.overlay,
    justifyContent: "center",
    alignItems: "center",
    paddingHorizontal: spacing[16]
  },
  modalCard: {
    width: "100%",
    maxWidth: 440,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    padding: spacing[16],
    gap: spacing[12]
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  qrWrap: {
    alignSelf: "center",
    padding: spacing[10],
    borderRadius: 6,
    backgroundColor: "#FFFFFF"
  },
  manualSecret: {
    color: palette.textPrimary,
    ...typography.body2,
    textAlign: "center"
  },
  recoveryCodes: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  recoveryCode: {
    width: "47%",
    color: palette.textPrimary,
    ...typography.body2,
    textAlign: "center",
    paddingVertical: spacing[6],
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: palette.border
  }
}));









