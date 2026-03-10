import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../feedback/ErrorState";
import { IconButton } from "../ui/IconButton";
import { ScreenContainer } from "../ui/ScreenContainer";
import { SkeletonBlock } from "../ui/SkeletonBlock";
import { palette, spacing, typography } from "../../theme/tokens";
import type { PolicyVersionDto } from "../../types/api";

type PolicyDocumentScreenProps = {
  title: string;
  policy: PolicyVersionDto | undefined;
  isLoading: boolean;
  errorMessage?: string;
  onRetry?: () => void;
};

function splitPolicyLines(content: string) {
  return content
    .split("\n")
    .map((line) => line.replace(/^([*\u2022\uFFFD]+)\s*/, "- ").trimEnd())
    .filter((line) => line.length > 0);
}

export function PolicyDocumentScreen({
  title,
  policy,
  isLoading,
  errorMessage,
  onRetry
}: PolicyDocumentScreenProps) {
  const router = useRouter();

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset scrollable={false}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>{title}</Text>
        <View style={styles.headerSpacer} />
      </View>

      {errorMessage ? (
        <ErrorState
          title={`Unable to load ${title.toLowerCase()}`}
          message={errorMessage}
          onRetry={onRetry}
        />
      ) : isLoading ? (
        <View style={styles.loadingWrap}>
          <SkeletonBlock style={{ height: 24, borderRadius: 8 }} />
          <SkeletonBlock style={{ height: 18, borderRadius: 8 }} />
          <SkeletonBlock style={{ height: 280, borderRadius: 12 }} />
        </View>
      ) : policy ? (
        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
          <Text style={styles.metaLine}>Version {policy.version}</Text>
          <Text style={styles.metaLine}>Effective {new Date(policy.effectiveUtc).toLocaleString()}</Text>
          {splitPolicyLines(policy.contentMarkdown).map((line, index) => {
            if (line.startsWith("### ")) {
              return (
                <Text key={`${line}-${index}`} style={styles.h3}>
                  {line.replace(/^###\s+/, "")}
                </Text>
              );
            }

            if (line.startsWith("## ")) {
              return (
                <Text key={`${line}-${index}`} style={styles.h2}>
                  {line.replace(/^##\s+/, "")}
                </Text>
              );
            }

            if (line.startsWith("# ")) {
              return (
                <Text key={`${line}-${index}`} style={styles.h1}>
                  {line.replace(/^#\s+/, "")}
                </Text>
              );
            }

            if (line.startsWith("- ")) {
              return (
                <Text key={`${line}-${index}`} style={styles.bulletLine}>
                  {`- ${line.slice(2)}`}
                </Text>
              );
            }

            return (
              <Text key={`${line}-${index}`} style={styles.bodyLine}>
                {line}
              </Text>
            );
          })}
        </ScrollView>
      ) : null}
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[16]
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
  loadingWrap: {
    gap: spacing[8]
  },
  scrollContent: {
    gap: spacing[8],
    paddingBottom: spacing[4]
  },
  metaLine: {
    color: palette.textSecondary,
    ...typography.caption
  },
  h1: {
    marginTop: spacing[8],
    color: palette.textPrimary,
    ...typography.title2
  },
  h2: {
    marginTop: spacing[8],
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  h3: {
    marginTop: spacing[4],
    color: palette.primaryGlow,
    ...typography.body2,
    fontWeight: "600"
  },
  bodyLine: {
    color: palette.textSecondary,
    ...typography.body2
  },
  bulletLine: {
    color: palette.textSecondary,
    ...typography.body2
  }
});
