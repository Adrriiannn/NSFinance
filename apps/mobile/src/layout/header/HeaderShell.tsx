import { StyleSheet, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { HeaderBackButton } from "./HeaderBackButton";
import { HEADER_CONSTANTS, HEADER_SURFACES } from "./header.constants";
import { HeaderDivider } from "./HeaderDivider";
import { HeaderMenuButton } from "./HeaderMenuButton";
import { HeaderPlaceholderAction } from "./HeaderPlaceholderAction";
import { headerPresets } from "./header.presets";
import { HeaderRow } from "./HeaderRow";
import { HeaderTitleBlock } from "./HeaderTitleBlock";
import type { HeaderShellProps } from "./header.types";

export function HeaderShell({
  preset,
  title,
  subtitle,
  includeTopInset = false,
  elevated = false,
  leadingAction,
  trailingAction,
  secondRow,
  style,
  contentStyle,
  hideDivider = false
}: HeaderShellProps) {
  const insets = useSafeAreaInsets();
  const config = headerPresets[preset];

  const resolvedLeading =
    leadingAction ?? (config.leading === "back" ? <HeaderBackButton /> : <HeaderMenuButton />);
  const resolvedTrailing =
    trailingAction ?? (config.preserveTrailingSlot ? <HeaderPlaceholderAction /> : null);

  return (
    <View
      style={[
        styles.shell,
        HEADER_SURFACES.shell,
        {
          paddingTop: includeTopInset ? insets.top : 0,
          opacity: elevated ? HEADER_CONSTANTS.stickyElevatedOpacity : 1
        },
        style
      ]}
    >
      <View style={[styles.content, contentStyle]}>
        <HeaderRow height={HEADER_CONSTANTS.rowHeight}>
          <View style={styles.leadingSlot}>{resolvedLeading}</View>
          <View style={styles.titleSlot}>
            <HeaderTitleBlock
              title={title}
              subtitle={subtitle}
              mode={config.titleMode}
              variant={config.titleVariant}
            />
          </View>
          <View style={styles.trailingSlot}>{resolvedTrailing}</View>
        </HeaderRow>

        {config.hasSecondRow && secondRow ? (
          <View style={styles.secondRowWrap}>
            <HeaderRow height={HEADER_CONSTANTS.secondRowHeight}>{secondRow}</HeaderRow>
          </View>
        ) : null}
      </View>

      <HeaderDivider visible={!hideDivider} />
    </View>
  );
}

const styles = StyleSheet.create({
  shell: {
    zIndex: HEADER_CONSTANTS.zIndex
  },
  content: {
    minHeight: HEADER_CONSTANTS.compactContentMinHeight
  },
  leadingSlot: {
    width: HEADER_CONSTANTS.leadingSlotWidth,
    minWidth: HEADER_CONSTANTS.leadingSlotWidth,
    alignItems: "flex-start",
    justifyContent: "center"
  },
  titleSlot: {
    flex: 1,
    alignItems: "stretch",
    justifyContent: "center"
  },
  trailingSlot: {
    width: HEADER_CONSTANTS.trailingSlotWidth,
    minWidth: HEADER_CONSTANTS.trailingSlotWidth,
    alignItems: "flex-end",
    justifyContent: "center"
  },
  secondRowWrap: {
    marginTop: HEADER_CONSTANTS.rowGap
  }
});

