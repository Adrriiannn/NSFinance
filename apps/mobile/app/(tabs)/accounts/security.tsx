import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocalSearchParams } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  View
} from "react-native";
import * as Sharing from "expo-sharing";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  getGoogleAuthOptions,
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
  useDisconnectBankConnectionMutation
} from "../../../src/features/banking/useBanking";
import { ApiClientError, formatUnknownError } from "../../../src/lib/api/errors";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import type { BankConnectionStatus } from "../../../src/types/api";
import { palette, spacing, typography } from "../../../src/theme/tokens";
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
  useCreateExportRequestMutation,
  useMyDeletionRequestsQuery,
  useMyExportRequestsQuery
} from "../../../src/features/support/useSupport";
import { downloadExportRequestFile } from "../../../src/features/support/supportApi";
import {
  useUpdateUserProfileMutation,
  useUserProfileQuery
} from "../../../src/features/users/useUserSettings";

const sessionKey = ["auth", "sessions"] as const;
const EXPORT_RETENTION_MS = 15 * 60 * 1000;

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

function formatBankConnectionStatus(status: BankConnectionStatus) {
  switch (status) {
    case "connected_pending_sync":
      return "Connected - sync starting";
    case "connected":
      return "Connected";
    case "sync_pending":
      return "Syncing";
    case "synced":
      return "Synced";
    case "reauth_required":
      return "Reconnect required";
    case "expired":
      return "Consent expired";
    default:
      return status;
  }
}

export default function SecuritySettingsScreen() {
  const params = useLocalSearchParams<{ focus?: string }>();
  const queryClient = useQueryClient();
  const profileQuery = useUserProfileQuery();
  const updateProfileMutation = useUpdateUserProfileMutation();
  const sessionsQuery = useQuery({ queryKey: sessionKey, queryFn: getSessions });
  const googleAuthQuery = useQuery({ queryKey: ["auth", "google-options"], queryFn: getGoogleAuthOptions });
  const connectedBanksQuery = useConnectedBanksQuery();
  const disconnectMutation = useDisconnectBankConnectionMutation();
  const exportRequestsQuery = useMyExportRequestsQuery();
  const deletionRequestsQuery = useMyDeletionRequestsQuery();
  const createExportMutation = useCreateExportRequestMutation();
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

  const [biometricEnabled, setBiometricEnabled] = useState(false);
  const [disconnectingConnectionId, setDisconnectingConnectionId] = useState<string | null>(null);

  const [passwordCodeModalVisible, setPasswordCodeModalVisible] = useState(false);
  const [passwordResetModalVisible, setPasswordResetModalVisible] = useState(false);
  const [passwordCode, setPasswordCode] = useState("");
  const [verifiedPasswordCode, setVerifiedPasswordCode] = useState("");
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
  const [timeNowMs, setTimeNowMs] = useState(() => Date.now());
  const securityScrollRef = useRef<ScrollView>(null);
  const currentScrollYRef = useRef(0);
  const scrollAnimationFrameRef = useRef<number | null>(null);
  const [exportSectionY, setExportSectionY] = useState<number | null>(null);
  const [hasAutoScrolledToExport, setHasAutoScrolledToExport] = useState(false);

  const cancelAutoScrollAnimation = useCallback(() => {
    if (scrollAnimationFrameRef.current !== null) {
      cancelAnimationFrame(scrollAnimationFrameRef.current);
      scrollAnimationFrameRef.current = null;
    }
  }, []);

  const animateScrollTo = useCallback(
    (targetY: number, durationMs = 900) => {
      cancelAutoScrollAnimation();

      const startY = currentScrollYRef.current;
      const delta = targetY - startY;
      if (Math.abs(delta) < 1) {
        securityScrollRef.current?.scrollTo({ y: targetY, animated: false });
        currentScrollYRef.current = targetY;
        return;
      }

      const startedAt = Date.now();
      const easeOutCubic = (t: number) => 1 - (1 - t) ** 3;

      const tick = () => {
        const elapsed = Date.now() - startedAt;
        const progress = Math.min(elapsed / durationMs, 1);
        const easedProgress = easeOutCubic(progress);
        const nextY = startY + delta * easedProgress;

        securityScrollRef.current?.scrollTo({ y: nextY, animated: false });
        currentScrollYRef.current = nextY;

        if (progress < 1) {
          scrollAnimationFrameRef.current = requestAnimationFrame(tick);
          return;
        }

        scrollAnimationFrameRef.current = null;
      };

      scrollAnimationFrameRef.current = requestAnimationFrame(tick);
    },
    [cancelAutoScrollAnimation]
  );

  const downloadExportMutation = useMutation({
    mutationFn: async (requestId: string) => {
      const uri = await downloadExportRequestFile(requestId);
      const canShare = await Sharing.isAvailableAsync();
      if (canShare) {
        await Sharing.shareAsync(uri, {
          mimeType: "application/json",
          dialogTitle: "Share your NSFinance export"
        });
      }

      return uri;
    },
    onSuccess: (uri) => {
      showFlashMessage(`Export package ready at ${uri}`, { tone: "success", durationMs: 2600 });
    },
    onError: async (error) => {
      if (
        error instanceof ApiClientError &&
        (error.code === "export_expired" || error.status === 410)
      ) {
        Alert.alert(
          "Export expired",
          "This download has expired. Please generate the file again."
        );
      } else {
        showFlashMessage(formatUnknownError(error), { tone: "error", durationMs: 2800 });
      }

      await queryClient.invalidateQueries({ queryKey: ["support", "export-requests"] });
    }
  });

  useEffect(() => {
    setBiometricEnabled(profileQuery.data?.biometricUnlockEnabled ?? false);
  }, [profileQuery.data?.biometricUnlockEnabled]);

  useEffect(() => {
    const timer = setInterval(() => {
      setTimeNowMs(Date.now());
    }, 30_000);

    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    if (params.focus === "data-export") {
      setHasAutoScrolledToExport(false);
    }
  }, [params.focus]);

  useEffect(() => {
    if (params.focus !== "data-export" || hasAutoScrolledToExport || exportSectionY === null) {
      return;
    }

    const timer = setTimeout(() => {
      animateScrollTo(Math.max(exportSectionY - spacing[16], 0));
      setHasAutoScrolledToExport(true);
    }, 120);

    return () => clearTimeout(timer);
  }, [animateScrollTo, params.focus, hasAutoScrolledToExport, exportSectionY]);

  useEffect(() => {
    return () => {
      cancelAutoScrollAnimation();
    };
  }, [cancelAutoScrollAnimation]);

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

  const persistBiometricSetting = async () => {
    if (!profileQuery.data) {
      return;
    }

    await updateProfileMutation.mutateAsync({
      primaryEmail: profileQuery.data.primaryEmail,
      fullName: profileQuery.data.fullName,
      displayName: profileQuery.data.displayName,
      handle: profileQuery.data.handle,
      profileImageUrl: profileQuery.data.profileImageUrl,
      profileSubtitle: profileQuery.data.profileSubtitle,
      timezone: profileQuery.data.timezone,
      locale: profileQuery.data.locale,
      preferredCurrency: profileQuery.data.preferredCurrency,
      onboardingStatus: profileQuery.data.onboardingStatus,
      biometricUnlockEnabled: biometricEnabled,
      twoFactorEnabled: profileQuery.data.twoFactorEnabled,
      phoneNumber: profileQuery.data.phoneNumber,
      dateOfBirth: profileQuery.data.dateOfBirth,
      countryRegion: profileQuery.data.countryRegion,
      financialFocus: profileQuery.data.financialFocus,
      employmentStatus: profileQuery.data.employmentStatus,
      incomeStability: profileQuery.data.incomeStability,
      primaryFinancialConcern: profileQuery.data.primaryFinancialConcern
    });

    showFlashMessage("Security settings updated.", { tone: "success" });
  };

  const startPasswordChangeFlow = async () => {
    setPasswordCode("");
    setPasswordCodeError(null);
    try {
      const response = await requestPasswordChangeCode();
      setPasswordCodeModalVisible(true);

      if (response.debugToken) {
        showFlashMessage(`Dev code: ${response.debugToken}`, { tone: "info", durationMs: 5000 });
      }
    } catch (error) {
      showFlashMessage(error instanceof Error ? error.message : "Could not request password code.", { tone: "error", durationMs: 3200 });
    }
  };

  const verifyCodeThenOpenPasswordModal = async () => {
    setPasswordCodeError(null);
    try {
      await verifyPasswordChangeCode({ code: passwordCode.trim() });
      setVerifiedPasswordCode(passwordCode.trim());
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
      || passwordBreachStatus !== "safe")
    {
      return;
    }

    try {
      await confirmPasswordChangeWithCode({
        code: verifiedPasswordCode,
        newPassword
      });

      setPasswordResetModalVisible(false);
      setNewPassword("");
      setConfirmNewPassword("");
      setVerifiedPasswordCode("");
      showFlashMessage("Password updated successfully.", { tone: "success" });
    } catch (error) {
      showFlashMessage(error instanceof Error ? error.message : "Could not update password.", { tone: "error", durationMs: 3200 });
    }
  };

  const requestDeletionCode = async () => {
    if (deletionConfirmationText.trim().toUpperCase() !== "DELETE") {
      Alert.alert("Confirmation required", "Type DELETE to continue with account deletion.");
      return;
    }

    try {
      const response = await requestAccountDeletionCode();
      setDeletionCode("");
      setDeletionCodeError(null);
      setDeletionCodeModalVisible(true);

      if (response.debugToken) {
        showFlashMessage(`Dev deletion code: ${response.debugToken}`, { tone: "info", durationMs: 5000 });
      }
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

  const handleDisconnectBank = async (connectionId: string) => {
    setDisconnectingConnectionId(connectionId);
    try {
      await disconnectMutation.mutateAsync(connectionId);
      showFlashMessage("Bank disconnected and imported data removed.", { tone: "success" });
    } catch (error) {
      showFlashMessage(formatUnknownError(error), { tone: "error", durationMs: 2800 });
    } finally {
      setDisconnectingConnectionId(null);
    }
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <HeaderShell preset="secondaryDetail" title="Security" />

      <ScrollView
        ref={securityScrollRef}
        showsVerticalScrollIndicator={false}
        contentContainerStyle={styles.scrollContent}
        onScroll={(event) => {
          currentScrollYRef.current = event.nativeEvent.contentOffset.y;
        }}
        scrollEventThrottle={16}
      >
        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Login & authentication</Text>
          <Text style={styles.metaLine}>
            Google sign-in: {googleAuthQuery.data?.isConfigured ? "Configured" : "Not configured"}
          </Text>
          <Text style={styles.metaLine}>2FA status: {profileQuery.data?.twoFactorEnabled ? "On" : "Off"}</Text>

          <View style={styles.toggleRow}>
            <Text style={styles.toggleLabel}>Biometric unlock</Text>
            <Switch
              value={biometricEnabled}
              onValueChange={setBiometricEnabled}
              thumbColor={palette.textPrimary}
              trackColor={{ false: "rgba(80,80,80,0.55)", true: "rgba(242,140,40,0.8)" }}
            />
          </View>

          <SecondaryButton
            label="Save biometric setting"
            onPress={() => {
              void persistBiometricSetting();
            }}
            disabled={updateProfileMutation.isPending}
          />

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
          <Text style={styles.hintText}>
            This list shows only currently usable bank links. Failed or abandoned attempts are hidden.
          </Text>
          {connectedBanksQuery.isError ? (
            <Text style={styles.errorText}>{formatUnknownError(connectedBanksQuery.error)}</Text>
          ) : activeBankConnections.length === 0 ? (
            <Text style={styles.metaLine}>No connected banks right now.</Text>
          ) : (
            activeBankConnections.map((connection) => (
              <View key={connection.id} style={styles.bankRow}>
                <Text style={styles.sessionTitle}>
                  {connection.providerDisplayName || connection.provider}
                </Text>
                <Text style={styles.metaLine}>Provider: {connection.provider}</Text>
                <Text style={styles.metaLine}>Connected: {formatDateTime(connection.createdUtc)}</Text>
                <Text style={styles.metaLine}>Last synced at: {formatDateTime(connection.lastSuccessfulSyncUtc)}</Text>
                <Text style={styles.metaLine}>Status: {formatBankConnectionStatus(connection.status)}</Text>
                <SecondaryButton
                  label={disconnectingConnectionId === connection.id ? "Disconnecting..." : "Disconnect bank"}
                  onPress={() => {
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
                  disabled={disconnectMutation.isPending}
                />
              </View>
            ))
          )}

          {attentionBankConnections.length > 0 ? (
            <>
              <Text style={styles.subSectionTitle}>Needs attention</Text>
              <Text style={styles.hintText}>
                These banks are no longer usable until they are reconnected. They are kept separate from active banks.
              </Text>
              {attentionBankConnections.map((connection) => (
                <View key={connection.id} style={styles.bankRow}>
                  <Text style={styles.sessionTitle}>
                    {connection.providerDisplayName || connection.provider}
                  </Text>
                  <Text style={styles.metaLine}>Provider: {connection.provider}</Text>
                  <Text style={styles.metaLine}>Connected: {formatDateTime(connection.createdUtc)}</Text>
                  <Text style={styles.metaLine}>Status: {formatBankConnectionStatus(connection.status)}</Text>
                  <SecondaryButton
                    label={disconnectingConnectionId === connection.id ? "Disconnecting..." : "Remove bank"}
                    onPress={() => {
                      Alert.alert(
                        "Remove bank",
                        "This removes the stale bank link and all imported data that came from it.",
                        [
                          { text: "Cancel", style: "cancel" },
                          {
                            text: "Remove",
                            style: "destructive",
                            onPress: () => {
                              void handleDisconnectBank(connection.id);
                            }
                          }
                        ]
                      );
                    }}
                    disabled={disconnectMutation.isPending}
                  />
                </View>
              ))}
            </>
          ) : null}
        </GlassCard>

        <View
          onLayout={(event) => {
            setExportSectionY(event.nativeEvent.layout.y);
          }}
        >
          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Data export</Text>
            <Text style={styles.hintText}>Request a downloadable package of your account and finance data.</Text>
            <PrimaryButton
              label="Generate JSON"
              onPress={() => {
                void createExportMutation.mutateAsync({
                  notes: "User requested data export from Security settings."
                });
              }}
              isLoading={createExportMutation.isPending}
            />
            {(exportRequestsQuery.data ?? []).slice(0, 1).map((request) => (
              <View key={request.id} style={styles.requestRow}>
                {(() => {
                  const requestedAtMs = new Date(request.requestedUtc).getTime();
                  const isTimeExpired =
                    request.status === "ready" &&
                    Number.isFinite(requestedAtMs) &&
                    timeNowMs - requestedAtMs >= EXPORT_RETENTION_MS;
                  const displayStatus = isTimeExpired ? "expired" : request.status;
                  const canDownload = request.status === "ready" && !isTimeExpired;

                  return (
                    <>
                      <Text style={styles.metaLine}>Status: {displayStatus}</Text>
                      <Text style={styles.metaLine}>Requested: {formatDateTime(request.requestedUtc)}</Text>
                      {canDownload ? (
                        <SecondaryButton
                          label="Download JSON"
                          onPress={() => {
                            void downloadExportMutation.mutateAsync(request.id);
                          }}
                          disabled={downloadExportMutation.isPending}
                        />
                      ) : null}
                    </>
                  );
                })()}
              </View>
            ))}
          </GlassCard>
        </View>

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

        {updateProfileMutation.isError ? (
          <Text style={styles.errorText}>{formatUnknownError(updateProfileMutation.error)}</Text>
        ) : null}
        {logoutAllMutation.isError ? (
          <Text style={styles.errorText}>{formatUnknownError(logoutAllMutation.error)}</Text>
        ) : null}
        {downloadExportMutation.isError ? (
          <Text style={styles.errorText}>{formatUnknownError(downloadExportMutation.error)}</Text>
        ) : null}
      </ScrollView>

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

const styles = StyleSheet.create({
  content: {
    paddingTop: 0
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
    backgroundColor: "rgba(21,21,21,0.6)",
    padding: spacing[12]
  },
  bankRow: {
    gap: spacing[8],
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: "rgba(21,21,21,0.6)",
    padding: spacing[12]
  },
  requestRow: {
    gap: spacing[8],
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: "rgba(21,21,21,0.6)",
    padding: spacing[12]
  },
  sessionTitle: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
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
    backgroundColor: "rgba(21,21,21,0.74)"
  },
  toggleLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: "rgba(9,9,9,0.74)",
    justifyContent: "center",
    alignItems: "center",
    paddingHorizontal: spacing[16]
  },
  modalCard: {
    width: "100%",
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(17,17,17,0.98)",
    padding: spacing[16],
    gap: spacing[12]
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  }
});








