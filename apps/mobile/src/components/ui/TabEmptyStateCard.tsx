import { StyleSheet, View } from "react-native";
import { spacing } from "../../theme/tokens";
import { EmptyState } from "./EmptyState";

type TabEmptyStateCardProps = {
  title: string;
  subtitle: string;
  ctaLabel?: string;
  onCtaPress?: () => void;
  verticalSpacingMode?: "tab-aligned" | "compact" | "none";
};

export function TabEmptyStateCard({
  title,
  subtitle,
  ctaLabel,
  onCtaPress,
  verticalSpacingMode = "tab-aligned"
}: TabEmptyStateCardProps) {
  const spacingStyle =
    verticalSpacingMode === "compact"
      ? styles.compact
      : verticalSpacingMode === "none"
        ? styles.none
        : styles.tabAligned;

  return (
    <View style={[styles.wrap, spacingStyle]}>
      <EmptyState
        title={title}
        message={subtitle}
        actionLabel={ctaLabel}
        onActionPress={onCtaPress}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    width: "100%"
  },
  tabAligned: {
    marginTop: spacing[20]
  },
  compact: {
    marginTop: spacing[12]
  },
  none: {
    marginTop: 0
  }
});
