import { ReactNode } from "react";
import {
  RefreshControl,
  ScrollView,
  StyleProp,
  StyleSheet,
  View,
  ViewStyle,
  type GestureResponderHandlers
} from "react-native";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { GlobalAppMenu } from "../layout/GlobalAppMenu";
import { useOptionalAdaptiveShell } from "../../layout/adaptive/adaptive.hooks";
import {
  CONTENT_FRAME_HORIZONTAL_PADDING,
  getDockAwareContentBottomInset,
  getPlainContentBottomInset
} from "../../layout/contentFrame";
import { layout, palette, spacing, surfaces } from "../../theme/tokens";

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
  const insets = useSafeAreaInsets();
  const adaptiveShell = useOptionalAdaptiveShell();
  const flattenedContentStyle = StyleSheet.flatten(contentStyle) ?? {};
  const menuTopOffset =
    typeof flattenedContentStyle.paddingTop === "number"
      ? flattenedContentStyle.paddingTop
      : spacing[8];
  const menuAbsoluteTop = insets.top + menuTopOffset;
  const computedBottomInset =
    (withBottomTabOffset
      ? getDockAwareContentBottomInset(insets.bottom)
      : getPlainContentBottomInset(insets.bottom)) + bottomInsetOffset;

  return (
    <SafeAreaView
      style={styles.safeArea}
      edges={includeBottomSafeArea ? ["top", "left", "right", "bottom"] : ["top", "left", "right"]}
      {...gestureHandlers}
    >
      {!adaptiveShell ? <GlobalAppMenu topOffset={menuAbsoluteTop} showTrigger={false} /> : null}

      {scrollable ? (
        <ScrollView
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
        </ScrollView>
      ) : (
        <View
          style={[
            styles.fixedContent,
            { paddingBottom: styles.fixedContent.paddingBottom + computedBottomInset },
            contentStyle
          ]}
        >
          {children}
        </View>
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
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
});
