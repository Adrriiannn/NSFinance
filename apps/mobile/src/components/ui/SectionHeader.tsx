import type { ReactNode } from "react";
import { StyleSheet, View } from "react-native";
import { spacing } from "../../theme/tokens";
import { TertiaryButton } from "./TertiaryButton";
import { AppText } from "./text/AppText";

type SectionHeaderProps = {
  title: string;
  subtitle?: string;
  actionLabel?: string;
  onActionPress?: () => void;
  trailing?: ReactNode;
};

export function SectionHeader({
  title,
  subtitle,
  actionLabel,
  onActionPress,
  trailing
}: SectionHeaderProps) {
  return (
    <View style={styles.row}>
      <View style={styles.titleWrap}>
        <AppText preset="sectionTitle" style={styles.titleText}>
          {title}
        </AppText>
        {subtitle ? (
          <AppText preset="secondary" style={styles.subtitleText}>
            {subtitle}
          </AppText>
        ) : null}
      </View>

      {trailing}

      {!trailing && actionLabel ? (
        <TertiaryButton label={actionLabel} onPress={onActionPress ?? (() => undefined)} />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  titleWrap: {
    flex: 1,
    minWidth: 0,
    gap: spacing[2]
  },
  titleText: {
    minWidth: 0,
    flexShrink: 1
  },
  subtitleText: {
    minWidth: 0,
    flexShrink: 1
  }
});
