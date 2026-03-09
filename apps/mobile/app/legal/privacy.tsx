import { useRouter } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { IconButton } from "../../src/components/ui/IconButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { useAiLimitationsPolicyQuery, usePrivacyPolicyQuery } from "../../src/features/policies/usePolicies";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function PrivacyScreen() {
  const router = useRouter();
  const privacyQuery = usePrivacyPolicyQuery();
  const aiQuery = useAiLimitationsPolicyQuery();

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.header}>
        <IconButton icon={<Text style={styles.back}>←</Text>} onPress={() => router.back()} />
        <Text style={styles.title}>Privacy Policy</Text>
        <View style={styles.headerSpacer} />
      </View>

      {privacyQuery.isError ? (
        <ErrorState
          title="Unable to load privacy policy"
          message={privacyQuery.error.message}
          onRetry={() => {
            void privacyQuery.refetch();
          }}
        />
      ) : privacyQuery.data ? (
        <View style={styles.body}>
          <Text style={styles.meta}>Version: {privacyQuery.data.version}</Text>
          <Text style={styles.meta}>
            Effective: {new Date(privacyQuery.data.effectiveUtc).toLocaleString()}
          </Text>
          <Text style={styles.paragraph}>
            Privacy content is referenced by: {privacyQuery.data.contentReference}
          </Text>
          {aiQuery.data ? (
            <Text style={styles.paragraph}>AI limitations notice: {aiQuery.data.contentReference}</Text>
          ) : null}
          <SecondaryButton label="Terms" onPress={() => router.push("/legal/terms" as never)} />
        </View>
      ) : (
        <Text style={styles.meta}>Loading privacy policy...</Text>
      )}
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[16],
    gap: spacing[16]
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  headerSpacer: {
    width: 42
  },
  back: {
    color: palette.textPrimary,
    ...typography.body1
  },
  title: {
    color: palette.textPrimary,
    ...typography.title2
  },
  body: {
    gap: spacing[12]
  },
  meta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  paragraph: {
    color: palette.textPrimary,
    ...typography.body2
  }
});
