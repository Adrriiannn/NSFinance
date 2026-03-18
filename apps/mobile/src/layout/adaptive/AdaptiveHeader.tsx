import { StyleSheet, Text, View } from "react-native";
import { palette, typography } from "../../theme/tokens";
import { ADAPTIVE_TOKENS } from "./adaptive.constants";
import { useAdaptiveShell } from "./adaptive.hooks";
import type { AdaptiveHeaderProps } from "./adaptive.types";

export function AdaptiveHeader({
  title,
  subtitle,
  leftAction,
  rightAction,
  centerContent,
  style
}: AdaptiveHeaderProps) {
  const { metrics } = useAdaptiveShell();
  const sideSlotSize = ADAPTIVE_TOKENS.menuTriggerSize;

  return (
    <View
      style={[
        styles.container,
        {
          paddingTop: metrics.safeAreaInsets.top + metrics.headerTopGap,
          paddingBottom: metrics.headerBottomGap
        },
        style
      ]}
    >
      <View style={styles.row}>
        <View style={[styles.sideSlot, { minWidth: sideSlotSize }]}>
          {leftAction ?? <View style={{ width: sideSlotSize, height: metrics.headerActionSize }} />}
        </View>
        <View style={styles.center}>
          {centerContent ? (
            centerContent
          ) : (
            <>
              {title ? <Text style={styles.title}>{title}</Text> : null}
              {subtitle ? (
                <Text style={[styles.subtitle, { marginTop: metrics.headerTitleGap }]}>
                  {subtitle}
                </Text>
              ) : null}
            </>
          )}
        </View>
        <View style={[styles.sideSlot, styles.rightSlot, { minWidth: sideSlotSize }]}>
          {rightAction ?? <View style={{ width: sideSlotSize, height: metrics.headerActionSize }} />}
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    width: "100%"
  },
  row: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 12
  },
  sideSlot: {
    minHeight: ADAPTIVE_TOKENS.menuTriggerSize,
    justifyContent: "center"
  },
  rightSlot: {
    alignItems: "flex-end"
  },
  center: {
    flex: 1,
    minHeight: ADAPTIVE_TOKENS.menuTriggerSize,
    justifyContent: "center"
  },
  title: {
    color: palette.textPrimary,
    ...typography.title2
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.body2
  }
});
