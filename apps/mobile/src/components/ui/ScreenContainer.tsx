import { ReactNode } from "react";
import {
  RefreshControl,
  ScrollView,
  StyleProp,
  StyleSheet,
  View,
  ViewStyle
} from "react-native";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { getFloatingTabBarInset } from "../../theme/insets";
import { layout, palette, spacing, surfaces } from "../../theme/tokens";

type ScreenContainerProps = {
  children: ReactNode;
  scrollable?: boolean;
  contentStyle?: StyleProp<ViewStyle>;
  refreshing?: boolean;
  onRefresh?: () => void;
  withBottomTabOffset?: boolean;
  bottomInsetOffset?: number;
};

export function ScreenContainer({
  children,
  scrollable = true,
  contentStyle,
  refreshing = false,
  onRefresh,
  withBottomTabOffset = false,
  bottomInsetOffset = 0
}: ScreenContainerProps) {
  const insets = useSafeAreaInsets();
  const floatingTabInset = withBottomTabOffset
    ? getFloatingTabBarInset(insets.bottom)
    : 0;
  const computedBottomInset = floatingTabInset + bottomInsetOffset;

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right", "bottom"]}>
      <View pointerEvents="none" style={styles.backgroundGlowTop} />
      <View pointerEvents="none" style={styles.backgroundGlowBottom} />

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
    paddingHorizontal: layout.screenHorizontalPadding,
    paddingBottom: spacing[32],
    gap: layout.sectionGap
  },
  fixedContent: {
    flex: 1,
    paddingHorizontal: layout.screenHorizontalPadding,
    paddingBottom: spacing[24]
  },
  backgroundGlowTop: {
    position: "absolute",
    top: -100,
    right: -60,
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: "rgba(47,107,255,0.09)"
  },
  backgroundGlowBottom: {
    position: "absolute",
    bottom: -160,
    left: -100,
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: "rgba(111,215,255,0.04)"
  }
});
