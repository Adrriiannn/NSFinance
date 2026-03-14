import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import {
  useAiDisclosurePolicyQuery,
  useDataRightsPolicyQuery,
  useOpenBankingDisclosurePolicyQuery,
  usePrivacyPolicyQuery,
  useTermsPolicyQuery
} from "../../../src/features/policies/usePolicies";
import { palette, spacing, typography } from "../../../src/theme/tokens";

function formatVersion(version?: string) {
  return version ? `v${version}` : "loading";
}

export default function LegalScreen() {
  const router = useRouter();
  const termsQuery = useTermsPolicyQuery();
  const privacyQuery = usePrivacyPolicyQuery();
  const openBankingQuery = useOpenBankingDisclosurePolicyQuery();
  const aiDisclosureQuery = useAiDisclosurePolicyQuery();
  const dataRightsQuery = useDataRightsPolicyQuery();

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
      onPress: () => router.push("/legal/privacy")
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

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Legal</Text>
        <View style={styles.headerSpacer} />
      </View>

      <Text style={styles.subtitle}>
        Review current legal documents and disclosures. Drafts are production-shaped and pending external legal review.
      </Text>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.listContent}>
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
      </ScrollView>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[16],
    gap: spacing[12]
  },
  listContent: {
    gap: spacing[12],
    paddingBottom: spacing[4]
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
  headerSpacer: {
    width: 42
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
  }
});


