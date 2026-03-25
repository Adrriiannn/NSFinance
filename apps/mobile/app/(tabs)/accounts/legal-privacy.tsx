import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { ScrollView, StyleSheet, Switch, Text, View } from "react-native";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  useAiDisclosurePolicyQuery,
  useConsentsQuery,
  useDataRightsPolicyQuery,
  useOpenBankingDisclosurePolicyQuery,
  usePrivacyPolicyQuery,
  useTermsPolicyQuery,
  useUpdateConsentMutation
} from "../../../src/features/policies/usePolicies";
import {
  useUpdateUserPreferencesMutation,
  useUserPreferencesQuery
} from "../../../src/features/users/useUserSettings";
import { useMyDeletionRequestsQuery } from "../../../src/features/support/useSupport";
import { showFlashMessage } from "../../../src/lib/flashMessage";
import { palette, spacing, typography } from "../../../src/theme/tokens";

function formatVersion(version?: string) {
  return version ? `v${version}` : "loading";
}

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

export default function LegalPrivacyScreen() {
  const router = useRouter();
  const termsQuery = useTermsPolicyQuery();
  const privacyQuery = usePrivacyPolicyQuery();
  const openBankingQuery = useOpenBankingDisclosurePolicyQuery();
  const aiDisclosureQuery = useAiDisclosurePolicyQuery();
  const dataRightsQuery = useDataRightsPolicyQuery();
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

  const items = [
    {
      key: "terms",
      title: "Terms of Service",
      description: "Service rules, eligibility, and limitations.",
      version: formatVersion(termsQuery.data?.version),
      onPress: () => router.push("/legal/terms")
    },
    {
      key: "privacy",
      title: "Privacy Policy",
      description: "How profile and financial data is processed.",
      version: formatVersion(privacyQuery.data?.version),
      onPress: () => router.push("/legal/privacy-policy")
    },
    {
      key: "open-banking",
      title: "Open Banking Disclosure",
      description: "Permissions, provider model, and disconnect rights.",
      version: formatVersion(openBankingQuery.data?.version),
      onPress: () => router.push("/legal/open-banking")
    },
    {
      key: "ai-disclosure",
      title: "AI Disclosure",
      description: "What AI features do and their limitations.",
      version: formatVersion(aiDisclosureQuery.data?.version),
      onPress: () => router.push("/legal/ai-disclosure")
    },
    {
      key: "data-rights",
      title: "Data Rights / GDPR Summary",
      description: "Export, deletion, correction, and privacy rights.",
      version: formatVersion(dataRightsQuery.data?.version),
      onPress: () => router.push("/legal/data-rights")
    }
  ];

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
        essentialCategoryPreferencesJson:
          preferencesQuery.data?.essentialCategoryPreferencesJson ?? "{}",
        futureGoalConfigurationJson: preferencesQuery.data?.futureGoalConfigurationJson ?? "{}"
      });

      await updateConsentMutation.mutateAsync({
        consentType: "marketing_communications",
        status: flags.analyticsEnabled ? "granted" : "denied",
        source: "legal_privacy",
        metadataJson: JSON.stringify({ analyticsEnabled: flags.analyticsEnabled })
      });

      showFlashMessage("Legal & privacy settings updated.", { tone: "success" });
    } catch (error) {
      showFlashMessage(
        error instanceof Error ? error.message : "Could not update legal & privacy settings.",
        { tone: "error", durationMs: 3200 }
      );
    }
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <HeaderShell preset="secondaryDetail" title="Legal & Privacy" />

      <Text style={styles.subtitle}>
        Review current legal documents and disclosures. Drafts are production-shaped and pending external legal review.
      </Text>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.listContent}>
        <GlassCard style={styles.itemCard}>
          <Text style={styles.itemTitle}>Privacy controls</Text>
          <Text style={styles.itemBody}>
            Manage how NSFinance uses your financial data for AI, personalization, and product analytics.
          </Text>

          <ToggleRow
            label="Allow AI to analyze my financial data"
            value={flags.aiAnalysisEnabled}
            onValueChange={(value) => setFlags((current) => ({ ...current, aiAnalysisEnabled: value }))}
          />
          <ToggleRow
            label="Allow AI-generated summaries"
            value={flags.aiSummariesEnabled}
            onValueChange={(value) => setFlags((current) => ({ ...current, aiSummariesEnabled: value }))}
          />
          <ToggleRow
            label="Allow AI-driven suggestions"
            value={flags.aiSuggestionsEnabled}
            onValueChange={(value) => setFlags((current) => ({ ...current, aiSuggestionsEnabled: value }))}
          />
          <ToggleRow
            label="Personalized insights"
            value={flags.personalizedInsightsEnabled}
            onValueChange={(value) =>
              setFlags((current) => ({ ...current, personalizedInsightsEnabled: value }))
            }
          />
          <ToggleRow
            label="Personalized notifications"
            value={flags.personalizedNotificationsEnabled}
            onValueChange={(value) =>
              setFlags((current) => ({ ...current, personalizedNotificationsEnabled: value }))
            }
          />
          <ToggleRow
            label="Anonymous analytics"
            value={flags.analyticsEnabled}
            onValueChange={(value) => setFlags((current) => ({ ...current, analyticsEnabled: value }))}
          />

          <Text style={styles.itemMeta}>Current communications consent: {marketingConsent}</Text>

          <SecondaryButton
            label="Save privacy controls"
            onPress={() => {
              void saveFlags();
            }}
            disabled={updatePreferencesMutation.isPending || updateConsentMutation.isPending}
          />
        </GlassCard>

        {items.map((item) => (
          <GlassCard key={item.key} style={styles.itemCard} onPress={item.onPress}>
            <Text style={styles.itemTitle}>{item.title}</Text>
            <Text style={styles.itemBody}>{item.description}</Text>
            <View style={styles.itemFooter}>
              <Text style={styles.itemMeta}>{item.version}</Text>
              <Ionicons name="chevron-forward" size={14} color={palette.textSecondary} />
            </View>
          </GlassCard>
        ))}

        <GlassCard style={styles.itemCard}>
          <Text style={styles.itemTitle}>Privacy rights</Text>
          <Text style={styles.itemBody}>
            Export requests and account deletion are handled from Security so identity verification stays in one place.
          </Text>
          <SecondaryButton
            label="Download my data"
            onPress={() =>
              router.push({
                pathname: "/(tabs)/accounts/security",
                params: { focus: "data-export" }
              })
            }
          />
          <SecondaryButton
            label="Delete my account"
            onPress={() => router.push("/(tabs)/accounts/security")}
          />
          {(deletionRequestsQuery.data ?? []).slice(0, 1).map((item) => (
            <Text key={item.id} style={styles.itemMeta}>
              Deletion request: {item.status} at {new Date(item.updatedUtc).toLocaleString("en-GB")}
            </Text>
          ))}
        </GlassCard>
      </ScrollView>
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
        trackColor={{ false: "rgba(80,80,80,0.55)", true: "rgba(242,140,40,0.8)" }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  content: {
    gap: spacing[12]
  },
  listContent: {
    gap: spacing[12],
    paddingBottom: spacing[4]
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.body2
  },
  itemCard: {
    gap: spacing[8]
  },
  itemTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  itemBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  itemFooter: {
    marginTop: spacing[4],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  itemMeta: {
    color: palette.primaryGlow,
    ...typography.caption
  },
  toggleRow: {
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: "rgba(21,21,21,0.72)",
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
  }
});



