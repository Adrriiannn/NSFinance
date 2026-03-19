import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import type { ReactElement, ReactNode } from "react";
import type {
  GestureResponderHandlers,
  LayoutRectangle,
  RefreshControlProps,
  StyleProp,
  ViewStyle
} from "react-native";
import type { EdgeInsets } from "react-native-safe-area-context";

export type AdaptiveWidthClass = "compact" | "regular" | "expanded";
export type AdaptiveHeightClass = "compact" | "regular" | "tall";

export type AdaptiveTabBarItem = {
  key: string;
  label: string;
  icon: string;
  iconFamily?: "ionicons" | "material";
};

export type AdaptiveTabBarMargins = {
  horizontal: number;
  bottom: number;
};

export type AdaptiveLayoutMetrics = {
  safeAreaInsets: EdgeInsets;
  screenWidth: number;
  screenHeight: number;
  widthClass: AdaptiveWidthClass;
  heightClass: AdaptiveHeightClass;
  usableWidth: number;
  usableHeight: number;
  contentHorizontalPadding: number;
  maxContentWidth: number;
  sectionGap: number;
  headerTopGap: number;
  headerBottomGap: number;
  headerTitleGap: number;
  headerActionSize: number;
  tabBarHeight: number;
  tabBarRadius: number;
  tabBarMargins: AdaptiveTabBarMargins;
  planningHubButtonSize: number;
  planningHubOverlap: number;
  planningHubLift: number;
  floatingAssistantSize: number;
  floatingAssistantRightMargin: number;
  floatingAssistantGapAboveTabBar: number;
  floatingAssistantBottomOffset: number;
  floatingAssistantDockedVisibleWidth: number;
  floatingAssistantDockAnimationDurationMs: number;
  floatingAssistantSwipeToDockThreshold: number;
  contentBottomInset: number;
};

export type AdaptiveShellFrame = LayoutRectangle & {
  top: number;
  bottom: number;
};

export type AdaptiveLayoutContextValue = {
  metrics: AdaptiveLayoutMetrics;
  shellFrame: AdaptiveShellFrame | null;
  setShellFrame: (frame: AdaptiveShellFrame | null) => void;
  markInteraction: () => void;
  getLastInteractionAt: () => number;
};

export type AdaptiveScreenProps = {
  children: ReactNode;
  scrollable?: boolean;
  contentStyle?: StyleProp<ViewStyle>;
  scrollContentStyle?: StyleProp<ViewStyle>;
  refreshControl?: ReactElement<RefreshControlProps>;
  gestureHandlers?: GestureResponderHandlers;
  showsVerticalScrollIndicator?: boolean;
  bounces?: boolean;
};

export type AdaptiveHeaderProps = {
  title?: string;
  subtitle?: string;
  leftAction?: ReactNode;
  rightAction?: ReactNode;
  centerContent?: ReactNode;
  style?: StyleProp<ViewStyle>;
};

export type PlanningHubDockButtonProps = {
  size: number;
  onPress: () => void;
  accessibilityLabel?: string;
};

export type FloatingAssistantDockProps = {
  onPress: () => void;
  accessibilityLabel?: string;
};

export type AdaptiveTabBarShellProps = {
  items: readonly AdaptiveTabBarItem[];
  activeKey: string;
  onPressItem: (item: AdaptiveTabBarItem) => void;
  hidden?: boolean;
  planningHubAction: {
    onPress: () => void;
    accessibilityLabel?: string;
  };
};

export type AdaptiveAppShellProps = {
  children: ReactNode;
};

export type AdaptiveBottomTabBarProps = BottomTabBarProps;
