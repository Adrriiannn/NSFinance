import { useRouter } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { IconButton } from "../../src/components/ui/IconButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { useAiLimitationsPolicyQuery } from "../../src/features/policies/usePolicies";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function AiLimitationsScreen() {
  const router = useRouter();
  const query = useAiLimitationsPolicyQuery();

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.header}>
        <IconButton icon={<Text style={styles.back}>←</Text>} onPress={() => router.back()} />
        <Text style={styles.title}>AI Limitations</Text>
        <View style={styles.headerSpacer} />
      </View>

      {query.isError ? (
        <ErrorState
          title="Unable to load AI notice"
          message={query.error.message}
          onRetry={() => {
            void query.refetch();
          }}
        />
      ) : query.data ? (
        <View style={styles.body}>
          <Text style={styles.meta}>Version: {query.data.version}</Text>
          <Text style={styles.meta}>Effective: {new Date(query.data.effectiveUtc).toLocaleString()}</Text>
          <Text style={styles.paragraph}>
            AI limitations content reference: {query.data.contentReference}
          </Text>
        </View>
      ) : (
        <Text style={styles.meta}>Loading AI limitations...</Text>
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
