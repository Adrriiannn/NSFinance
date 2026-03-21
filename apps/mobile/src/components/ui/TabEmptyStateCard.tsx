import { StyleSheet, View } from "react-native";
import { spacing } from "../../theme/tokens";
import { EmptyState } from "./EmptyState";

type TabEmptyStateCardProps = {
  title: string;
  subtitle: string;
  ctaLabel?: string;
  onCtaPress?: () => void;
  verticalSpacingMode?: "tab-aligned" | "compact" | "none";
  hideOrb?: boolean;
  centerText?: boolean;
};

export function TabEmptyStateCard({
  title,
  subtitle,
  ctaLabel,
  onCtaPress,
  verticalSpacingMode = "tab-aligned",
  hideOrb = false,
  centerText = false
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
        hideOrb={hideOrb}
        centerText={centerText}
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
