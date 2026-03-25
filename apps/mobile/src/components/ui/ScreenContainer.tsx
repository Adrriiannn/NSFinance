import { useMemo, type ReactNode } from "react";
import {
  RefreshControl,
  StyleProp,
  StyleSheet,
  ViewStyle,
  type GestureResponderHandlers
} from "react-native";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { GlobalAppMenu } from "../layout/GlobalAppMenu";
import { useOptionalAdaptiveShell } from "../../layout/adaptive/adaptive.hooks";
import {
  CONTENT_FRAME_DOCK_GAP,
  CONTENT_FRAME_HORIZONTAL_PADDING,
} from "../../layout/contentFrame";
import { useThemeTokens } from "../../theme/tokens";
import { useRuntimeBottomInsetPolicy } from "../../theme/insets";
import { AppBackgroundLayer } from "./surfaces/AppBackgroundLayer";
import { BottomInsetAwareScrollView } from "./insets/BottomInsetAwareScrollView";
import { BottomInsetAwareView } from "./insets/BottomInsetAwareView";

type ScreenContainerProps = {
  children: ReactNode;
  scrollable?: boolean;
  contentStyle?: StyleProp<ViewStyle>;
  refreshing?: boolean;
  onRefresh?: () => void;
  withBottomTabOffset?: boolean;
  bottomInsetOffset?: number;
  includeBottomSafeArea?: boolean;
  gestureHandlers?: GestureResponderHandlers;
};

export function ScreenContainer({
  children,
  scrollable = true,
  contentStyle,
  refreshing = false,
  onRefresh,
  withBottomTabOffset = false,
  bottomInsetOffset = 0,
  includeBottomSafeArea = !withBottomTabOffset,
  gestureHandlers
}: ScreenContainerProps) {
  const { layout, navigation, palette, spacing, surfaces } = useThemeTokens();
  const styles = useMemo(
    () =>
      StyleSheet.create({
        safeArea: {
          flex: 1,
          backgroundColor: surfaces.app
        },
        scrollContent: {
          paddingHorizontal: CONTENT_FRAME_HORIZONTAL_PADDING,
          paddingBottom: 0,
          gap: layout.sectionGap
        },
        fixedContent: {
          flex: 1,
          paddingHorizontal: CONTENT_FRAME_HORIZONTAL_PADDING,
          paddingBottom: 0
        }
      }),
    [layout, surfaces]
  );
  const insets = useSafeAreaInsets();
  const bottomInsetPolicy = useRuntimeBottomInsetPolicy();
  const adaptiveShell = useOptionalAdaptiveShell();
  const flattenedContentStyle = StyleSheet.flatten(contentStyle) ?? {};
  const menuTopOffset =
    typeof flattenedContentStyle.paddingTop === "number"
      ? flattenedContentStyle.paddingTop
      : spacing[8];
  const menuAbsoluteTop = insets.top + menuTopOffset;
  const safeAreaBottomCompensation = includeBottomSafeArea
    ? -bottomInsetPolicy.bottomContentInset
    : 0;
  const computedBottomInset =
    (withBottomTabOffset
      ? navigation.floatingTabBarHeight + CONTENT_FRAME_DOCK_GAP
      : 0) + bottomInsetOffset + safeAreaBottomCompensation;

  return (
    <SafeAreaView
      style={styles.safeArea}
      edges={includeBottomSafeArea ? ["top", "left", "right", "bottom"] : ["top", "left", "right"]}
      {...gestureHandlers}
    >
      <AppBackgroundLayer />
      {!adaptiveShell ? <GlobalAppMenu topOffset={menuAbsoluteTop} showTrigger={false} /> : null}

      {scrollable ? (
        <BottomInsetAwareScrollView
          mode="content"
          contentContainerStyle={[
            styles.scrollContent,
            { paddingBottom: styles.scrollContent.paddingBottom + computedBottomInset },
            contentStyle
          ]}
          showsVerticalScrollIndicator={false}
          refreshControl={
            onRefresh ? (
              <RefreshControl
                refreshing={refreshing}
                onRefresh={onRefresh}
                tintColor={palette.textSecondary}
              />
            ) : undefined
          }
        >
          {children}
        </BottomInsetAwareScrollView>
      ) : (
        <BottomInsetAwareView
          mode="content"
          style={[
            styles.fixedContent,
            { paddingBottom: styles.fixedContent.paddingBottom + computedBottomInset },
            contentStyle
          ]}
        >
          {children}
        </BottomInsetAwareView>
      )}
    </SafeAreaView>
  );
}
