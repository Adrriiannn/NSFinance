import { useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { ScrollView, StyleSheet, Switch, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  useConsentsQuery,
  useUpdateConsentMutation
} from "../../../src/features/policies/usePolicies";
import {
  useUpdateUserPreferencesMutation,
  useUserPreferencesQuery
} from "../../../src/features/users/useUserSettings";
import {
  useMyDeletionRequestsQuery
} from "../../../src/features/support/useSupport";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import { palette, spacing, typography } from "../../../src/theme/tokens";

type PrivacyFlags = {
  aiAnalysisEnabled: boolean;
  aiSummariesEnabled: boolean;
  aiSuggestionsEnabled: boolean;
  personalizedInsightsEnabled: boolean;
  personalizedNotificationsEnabled: boolean;
  analyticsEnabled: boolean;
};

const defaultFlags: PrivacyFlags = {
  aiAnalysisEnabled: true,
  aiSummariesEnabled: true,
  aiSuggestionsEnabled: true,
  personalizedInsightsEnabled: true,
  personalizedNotificationsEnabled: true,
  analyticsEnabled: true
};

function parseJson<T>(value: string, fallback: T): T {
  try {
    const parsed = JSON.parse(value) as T;
    if (!parsed || typeof parsed !== "object") {
      return fallback;
    }

    return parsed;
  } catch {
    return fallback;
  }
}

export default function PrivacySettingsScreen() {
  const router = useRouter();
  const preferencesQuery = useUserPreferencesQuery();
  const updatePreferencesMutation = useUpdateUserPreferencesMutation();
  const consentsQuery = useConsentsQuery();
  const updateConsentMutation = useUpdateConsentMutation();
  const deletionRequestsQuery = useMyDeletionRequestsQuery();

  const [flags, setFlags] = useState<PrivacyFlags>(defaultFlags);

  useEffect(() => {
    if (!preferencesQuery.data) {
      return;
    }

    const privacyJson = parseJson<Partial<PrivacyFlags>>(
      preferencesQuery.data.privacyPreferencesJson,
      {}
    );

    const notificationJson = parseJson<Partial<PrivacyFlags>>(
      preferencesQuery.data.notificationPreferencesJson,
      {}
    );

    setFlags({
      aiAnalysisEnabled: privacyJson.aiAnalysisEnabled ?? true,
      aiSummariesEnabled: privacyJson.aiSummariesEnabled ?? true,
      aiSuggestionsEnabled: privacyJson.aiSuggestionsEnabled ?? true,
      personalizedInsightsEnabled: privacyJson.personalizedInsightsEnabled ?? true,
      personalizedNotificationsEnabled: notificationJson.personalizedNotificationsEnabled ?? true,
      analyticsEnabled: privacyJson.analyticsEnabled ?? true
    });
  }, [preferencesQuery.data]);

  const marketingConsent = useMemo(
    () =>
      consentsQuery.data?.find((item) => item.consentType === "marketing_communications")?.status ??
      "denied",
    [consentsQuery.data]
  );

  const saveFlags = async () => {
    try {
      await updatePreferencesMutation.mutateAsync({
        adviceTonePreference: preferencesQuery.data?.adviceTonePreference ?? "balanced",
        digestFrequency: preferencesQuery.data?.digestFrequency ?? "weekly",
        reminderPreference: preferencesQuery.data?.reminderPreference ?? "important_only",
        notificationPreferencesJson: JSON.stringify({
          personalizedNotificationsEnabled: flags.personalizedNotificationsEnabled
        }),
        privacyPreferencesJson: JSON.stringify({
          aiAnalysisEnabled: flags.aiAnalysisEnabled,
          aiSummariesEnabled: flags.aiSummariesEnabled,
          aiSuggestionsEnabled: flags.aiSuggestionsEnabled,
          personalizedInsightsEnabled: flags.personalizedInsightsEnabled,
          analyticsEnabled: flags.analyticsEnabled
        }),
        essentialCategoryPreferencesJson: preferencesQuery.data?.essentialCategoryPreferencesJson ?? "{}",
        futureGoalConfigurationJson: preferencesQuery.data?.futureGoalConfigurationJson ?? "{}"
      });

      await updateConsentMutation.mutateAsync({
        consentType: "marketing_communications",
        status: flags.analyticsEnabled ? "granted" : "denied",
        source: "privacy_settings",
        metadataJson: JSON.stringify({ analyticsEnabled: flags.analyticsEnabled })
      });

      showFlashMessage("Privacy settings updated.", { tone: "success" });
    } catch (error) {
      showFlashMessage(error instanceof Error ? error.message : "Could not update privacy settings.", {
        tone: "error",
        durationMs: 3200
      });
    }
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <HeaderShell preset="secondaryDetail" title="Privacy" />

      {preferencesQuery.isError || consentsQuery.isError ? (
        <ErrorState
          title="Could not load privacy settings"
          message={preferencesQuery.error?.message ?? consentsQuery.error?.message ?? "Unknown error"}
          onRetry={() => {
            void preferencesQuery.refetch();
            void consentsQuery.refetch();
          }}
        />
      ) : (
        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Financial data processing</Text>
            <Text style={styles.sectionBody}>
              NSFinance uses linked bank balances, transaction history, account metadata, and provider connection status to power account views, spending analysis, and sync diagnostics.
            </Text>
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>AI data usage</Text>
            <ToggleRow
              label="Allow AI to analyze my financial data"
              value={flags.aiAnalysisEnabled}
              onValueChange={(value) => setFlags((current) => ({ ...current, aiAnalysisEnabled: value }))}
            />
            <ToggleRow
              label="Allow AI-generated financial summaries"
              value={flags.aiSummariesEnabled}
              onValueChange={(value) => setFlags((current) => ({ ...current, aiSummariesEnabled: value }))}
            />
            <ToggleRow
              label="Allow AI-driven suggestions"
              value={flags.aiSuggestionsEnabled}
              onValueChange={(value) => setFlags((current) => ({ ...current, aiSuggestionsEnabled: value }))}
            />
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Personalization</Text>
            <ToggleRow
              label="Personalized insights"
              value={flags.personalizedInsightsEnabled}
              onValueChange={(value) => setFlags((current) => ({ ...current, personalizedInsightsEnabled: value }))}
            />
            <ToggleRow
              label="Personalized notifications"
              value={flags.personalizedNotificationsEnabled}
              onValueChange={(value) => setFlags((current) => ({ ...current, personalizedNotificationsEnabled: value }))}
            />
            <ToggleRow
              label="Anonymous analytics"
              value={flags.analyticsEnabled}
              onValueChange={(value) => setFlags((current) => ({ ...current, analyticsEnabled: value }))}
            />
            <Text style={styles.metaLine}>Current communications consent: {marketingConsent}</Text>
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Data retention summary</Text>
            <Text style={styles.sectionBody}>
              Data is retained according to operational and legal requirements. Export and deletion requests can be created from Security settings and are tracked with request statuses.
            </Text>
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Third-party services</Text>
            <Text style={styles.metaLine}>Open Banking provider: TrueLayer</Text>
            <Text style={styles.metaLine}>
              Cloud infrastructure: Microsoft Azure services are used to host app data and backend services.
            </Text>
            <Text style={styles.metaLine}>
              Support tooling: used to receive and manage support requests when you contact us.
            </Text>
            <Text style={styles.metaLine}>
              AI infrastructure: Microsoft Azure OpenAI services may be used when AI features are enabled.
            </Text>
          </GlassCard>

          <GlassCard style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Privacy rights</Text>
            <SecondaryButton
              label="Download my data"
              onPress={() =>
                router.push({
                  pathname: "/(tabs)/accounts/security",
                  params: { focus: "data-export" }
                })
              }
            />
            <SecondaryButton label="Delete my account" onPress={() => router.push("/(tabs)/accounts/security")} />
            <SecondaryButton label="Legal Info" onPress={() => router.push("/(tabs)/accounts/legal")} />
            {(deletionRequestsQuery.data ?? []).slice(0, 1).map((item) => (
              <Text key={item.id} style={styles.metaLine}>
                Deletion request: {item.status} at {new Date(item.updatedUtc).toLocaleString("en-GB")}
              </Text>
            ))}
          </GlassCard>

          <PrimaryButton
            label="Save privacy settings"
            onPress={() => {
              void saveFlags();
            }}
            isLoading={updatePreferencesMutation.isPending || updateConsentMutation.isPending}
          />
        </ScrollView>
      )}
    </ScreenContainer>
  );
}

function ToggleRow({
  label,
  value,
  onValueChange
}: {
  label: string;
  value: boolean;
  onValueChange: (next: boolean) => void;
}) {
  return (
    <View style={styles.toggleRow}>
      <Text style={styles.toggleLabel}>{label}</Text>
      <Switch
        value={value}
        onValueChange={onValueChange}
        thumbColor={palette.textPrimary}
        trackColor={{ false: "rgba(134,154,184,0.4)", true: "rgba(47,107,255,0.8)" }}
      />
    </View>
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
  sectionBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  metaLine: {
    color: palette.textSecondary,
    ...typography.caption
  },
  toggleRow: {
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 12,
    backgroundColor: "rgba(18,36,58,0.72)",
    minHeight: 44,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  toggleLabel: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body2
  },
  saveText: {
    color: palette.success,
    ...typography.caption
  }
});


