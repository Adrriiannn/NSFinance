import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { Animated, Pressable, StyleSheet, Text, View } from "react-native";
import Svg, { Path } from "react-native-svg";
import { PEEK_UNDER_BAR_TRANSLATE_ADJUSTMENT } from "./planningHubPeek.constants";
import { borders, palette, radius, spacing, typography } from "../../theme/tokens";

type PlanningHubPeekAction = {
  label: string;
  icon: string;
  iconFamily?: "ionicons" | "material";
  onPress: () => void;
  accessibilityLabel?: string;
};

type PlanningHubPeekButtonProps = {
  action: PlanningHubPeekAction;
  left?: number | null;
  translateY: Animated.Value;
  opacity: Animated.Value;
  scale: Animated.Value;
  interactive: boolean;
  placement?: "above" | "under";
  bottomOffset?: number;
};

const TAB_BAR_SEAM_COLOR = "rgba(242, 140, 40, 0.24)";
const SWITCHER_WIDTH = 272;
const SWITCHER_HEIGHT = 70;
const SWITCHER_VISIBLE_WIDTH = 152;
const SWITCHER_BOTTOM_CROP = 0.5;
const SWITCHER_BUTTON_WIDTH = 122;
const SWITCHER_BUTTON_HEIGHT = 48;
const SWITCHER_FILL_INSET = 34;
const SWITCHER_FILL_BASELINE = 64;
const SWITCHER_STROKE_HEIGHT = 64;
const SWITCHER_SIDE_CROP = (SWITCHER_WIDTH - SWITCHER_VISIBLE_WIDTH) / 2;
const SWITCHER_SHAPE_STROKE_PATH =
  "M0 70 C36 70 58 69 72 58 C82 50 88 40 92 24 C96 10 102 4 114 4 L158 4 C170 4 176 10 180 24 C184 40 190 50 200 58 C214 69 236 70 272 70";
const SWITCHER_SHAPE_FILL_PATH = `${SWITCHER_SHAPE_STROKE_PATH} L${SWITCHER_WIDTH - SWITCHER_FILL_INSET} ${SWITCHER_FILL_BASELINE} L${SWITCHER_FILL_INSET} ${SWITCHER_FILL_BASELINE} Z`;
const SWITCHER_SEAM_PATH = `M${SWITCHER_SIDE_CROP} ${SWITCHER_FILL_BASELINE} H${SWITCHER_WIDTH - SWITCHER_SIDE_CROP}`;

function renderActionIcon(
  action: Pick<PlanningHubPeekAction, "icon" | "iconFamily">,
  color: string,
  size: number
) {
  if (action.iconFamily === "material") {
    return (
      <MaterialCommunityIcons
        name={action.icon as keyof typeof MaterialCommunityIcons.glyphMap}
        size={size}
        color={color}
      />
    );
  }

  return (
    <Ionicons
      name={action.icon as keyof typeof Ionicons.glyphMap}
      size={size}
      color={color}
    />
  );
}

export function PlanningHubPeekButton({
  action,
  left,
  translateY,
  opacity,
  scale,
  interactive,
  placement = "above",
  bottomOffset
}: PlanningHubPeekButtonProps) {
  const resolvedTranslateY =
    placement === "under"
      ? Animated.add(translateY, PEEK_UNDER_BAR_TRANSLATE_ADJUSTMENT)
      : translateY;

  return (
    <Animated.View
      pointerEvents={interactive ? "box-none" : "none"}
      style={[
        placement === "under" ? styles.switcherMountUnder : styles.switcherMountAbove,
        placement === "under" && bottomOffset !== undefined ? { bottom: bottomOffset } : null,
        left !== null && left !== undefined ? { left, marginLeft: 0 } : null,
        {
          opacity,
          transform: [{ translateY: resolvedTranslateY }, { scale }]
        }
      ]}
    >
      <View pointerEvents="none" style={styles.switcherShape}>
        <Svg
          width={SWITCHER_WIDTH}
          height={SWITCHER_HEIGHT + 1}
          viewBox={`0 0 ${SWITCHER_WIDTH} ${SWITCHER_HEIGHT + 1}`}
          style={styles.switcherSvg}
        >
          <Path d={SWITCHER_SHAPE_FILL_PATH} fill={palette.tabBarSurface} />
        </Svg>
        <View style={styles.switcherStrokeCrop}>
          <Svg
            width={SWITCHER_WIDTH}
            height={SWITCHER_HEIGHT + 1}
            viewBox={`0 0 ${SWITCHER_WIDTH} ${SWITCHER_HEIGHT + 1}`}
            style={styles.switcherSvg}
          >
            <Path
              d={SWITCHER_SHAPE_STROKE_PATH}
              fill="none"
              stroke={TAB_BAR_SEAM_COLOR}
              strokeWidth={borders.width.thin}
              strokeLinecap="round"
              strokeLinejoin="round"
            />
            <Path
              d={SWITCHER_SEAM_PATH}
              fill="none"
              stroke={TAB_BAR_SEAM_COLOR}
              strokeWidth={borders.width.thin}
              strokeLinecap="butt"
            />
          </Svg>
        </View>
      </View>

      <Pressable
        accessibilityRole="button"
        accessibilityLabel={action.accessibilityLabel ?? action.label}
        disabled={!interactive}
        onPress={action.onPress}
        style={({ pressed }) => [
          styles.switcherButton,
          pressed && interactive ? styles.switcherButtonPressed : null
        ]}
      >
        {renderActionIcon(action, palette.textSecondary, 18)}
        <Text style={styles.switcherLabel} numberOfLines={1}>
          {action.label}
        </Text>
      </Pressable>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  switcherMountAbove: {
    position: "absolute",
    bottom: "100%",
    left: "50%",
    marginLeft: -SWITCHER_WIDTH / 2,
    width: SWITCHER_WIDTH,
    height: SWITCHER_HEIGHT,
    alignItems: "center",
    justifyContent: "flex-start",
    zIndex: 3
  },
  switcherMountUnder: {
    position: "absolute",
    bottom: 86,
    left: "50%",
    marginLeft: -SWITCHER_WIDTH / 2,
    width: SWITCHER_WIDTH,
    height: SWITCHER_HEIGHT,
    alignItems: "center",
    justifyContent: "flex-start",
    zIndex: 1
  },
  switcherShape: {
    position: "absolute",
    bottom: 0,
    left: "50%",
    marginLeft: -SWITCHER_VISIBLE_WIDTH / 2,
    width: SWITCHER_VISIBLE_WIDTH,
    height: SWITCHER_HEIGHT + 1 - SWITCHER_BOTTOM_CROP,
    overflow: "hidden"
  },
  switcherSvg: {
    marginLeft: -SWITCHER_SIDE_CROP
  },
  switcherStrokeCrop: {
    position: "absolute",
    left: 0,
    right: 0,
    top: 0,
    height: SWITCHER_STROKE_HEIGHT,
    overflow: "hidden"
  },
  switcherButton: {
    position: "absolute",
    top: spacing[10],
    left: "50%",
    marginLeft: -SWITCHER_BUTTON_WIDTH / 2,
    width: SWITCHER_BUTTON_WIDTH,
    minHeight: SWITCHER_BUTTON_HEIGHT,
    paddingHorizontal: spacing[10],
    paddingTop: spacing[4],
    paddingBottom: spacing[8],
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[4],
    borderRadius: radius.pill
  },
  switcherButtonPressed: {
    opacity: 0.8,
    transform: [{ scale: 0.98 }]
  },
  switcherLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "600",
    textAlign: "center"
  }
});
