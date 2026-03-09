import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import {
  useAcceptPolicyMutation,
  usePolicyAcceptancesQuery,
  usePrivacyPolicyQuery,
  useTermsPolicyQuery
} from "../../../src/features/policies/usePolicies";
import { palette, spacing, typography } from "../../../src/theme/tokens";

export default function LegalScreen() {
  const router = useRouter();
  const termsQuery = useTermsPolicyQuery();
  const privacyQuery = usePrivacyPolicyQuery();
  const acceptancesQuery = usePolicyAcceptancesQuery();
  const acceptMutation = useAcceptPolicyMutation();

  const acceptPolicy = async (policyType: string, policyVersion: string) => {
    await acceptMutation.mutateAsync({
      policyType,
      policyVersion,
      acceptanceContext: "in_app_settings",
      platform: "mobile"
    });
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Legal & Acceptances</Text>
        <View style={{ width: 42 }} />
      </View>

      {acceptancesQuery.isError ? (
        <ErrorState
          title="Could not load acceptances"
          message={acceptancesQuery.error.message}
          onRetry={() => {
            void acceptancesQuery.refetch();
          }}
        />
      ) : (
        <GlassCard style={styles.card}>
          <Text style={styles.sectionTitle}>Recorded Acceptances</Text>
          {(acceptancesQuery.data ?? []).length === 0 ? (
            <Text style={styles.meta}>No policy acceptances recorded yet.</Text>
          ) : (
            (acceptancesQuery.data ?? []).map((item) => (
              <View key={`${item.policyType}-${item.policyVersion}`} style={styles.acceptanceRow}>
                <Text style={styles.body}>
                  {item.policyType} v{item.policyVersion}
                </Text>
                <Text style={styles.meta}>{new Date(item.acceptedUtc).toLocaleString()}</Text>
              </View>
            ))
          )}
        </GlassCard>
      )}

      <GlassCard style={styles.card}>
        <Text style={styles.sectionTitle}>Current Policies</Text>
        <Text style={styles.meta}>
          Terms: {termsQuery.data?.version ?? "loading"} | Privacy: {privacyQuery.data?.version ?? "loading"}
        </Text>
        <PrimaryButton
          label="Accept current Terms"
          onPress={() => {
            if (termsQuery.data) {
              void acceptPolicy("terms_of_service", termsQuery.data.version);
            }
          }}
          disabled={!termsQuery.data}
          isLoading={acceptMutation.isPending}
        />
        <PrimaryButton
          label="Accept current Privacy"
          onPress={() => {
            if (privacyQuery.data) {
              void acceptPolicy("privacy_policy", privacyQuery.data.version);
            }
          }}
          disabled={!privacyQuery.data}
          isLoading={acceptMutation.isPending}
        />
      </GlassCard>
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
  card: {
    gap: spacing[12]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  acceptanceRow: {
    gap: spacing[4]
  },
  body: {
    color: palette.textPrimary,
    ...typography.body2
  },
  meta: {
    color: palette.textSecondary,
    ...typography.caption
  }
});
