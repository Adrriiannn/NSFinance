import { Ionicons } from "@expo/vector-icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { Alert, StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import {
  getSessions,
  logoutAll,
  revokeSession
} from "../../../src/features/auth/authApi";
import { palette, spacing, typography } from "../../../src/theme/tokens";

const sessionKey = ["auth", "sessions"] as const;

export default function SessionsScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const sessionsQuery = useQuery({
    queryKey: sessionKey,
    queryFn: getSessions
  });

  const revokeMutation = useMutation({
    mutationFn: (sessionId: string) => revokeSession(sessionId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: sessionKey });
    }
  });

  const logoutAllMutation = useMutation({
    mutationFn: () => logoutAll(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: sessionKey });
    }
  });

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Sessions & Devices</Text>
        <View style={{ width: 42 }} />
      </View>

      {sessionsQuery.isError ? (
        <ErrorState
          title="Could not load sessions"
          message={sessionsQuery.error.message}
          onRetry={() => {
            void sessionsQuery.refetch();
          }}
        />
      ) : (
        <View style={styles.list}>
          {(sessionsQuery.data ?? []).map((session) => (
            <GlassCard key={session.id} style={styles.sessionCard}>
              <Text style={styles.sessionTitle}>
                {session.deviceLabel} {session.isCurrentSession ? "(current)" : ""}
              </Text>
              <Text style={styles.sessionMeta}>
                {session.platform ?? "unknown"} | last seen {new Date(session.lastSeenUtc).toLocaleString()}
              </Text>
              <Text style={styles.sessionMeta}>
                Expires {new Date(session.expiresUtc).toLocaleString()}
              </Text>
              {session.revokedUtc ? (
                <Text style={styles.revoked}>Revoked {new Date(session.revokedUtc).toLocaleString()}</Text>
              ) : (
                <SecondaryButton
                  label="Revoke session"
                  onPress={() => {
                    void revokeMutation.mutateAsync(session.id);
                  }}
                  disabled={session.isCurrentSession || revokeMutation.isPending}
                />
              )}
            </GlassCard>
          ))}
        </View>
      )}

      <PrimaryButton
        label="Logout all other sessions"
        onPress={() => {
          Alert.alert(
            "Logout all sessions",
            "This keeps only your current session active.",
            [
              { text: "Cancel", style: "cancel" },
              {
                text: "Confirm",
                style: "destructive",
                onPress: () => {
                  void logoutAllMutation.mutateAsync();
                }
              }
            ]
          );
        }}
        isLoading={logoutAllMutation.isPending}
      />
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[16],
    gap: spacing[16]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  list: {
    gap: spacing[12]
  },
  sessionCard: {
    gap: spacing[8]
  },
  sessionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  sessionMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  revoked: {
    color: palette.caution,
    ...typography.caption
  }
});
