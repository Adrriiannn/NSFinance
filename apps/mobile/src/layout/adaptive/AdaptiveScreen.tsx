import { ScrollView, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { surfaces, createRuntimeStyleSheet } from "../../theme/tokens";
import { useAdaptiveShell } from "./adaptive.hooks";
import type { AdaptiveScreenProps } from "./adaptive.types";

export function AdaptiveScreen({
  children,
  scrollable = false,
  contentStyle,
  scrollContentStyle,
  refreshControl,
  gestureHandlers,
  showsVerticalScrollIndicator = false,
  bounces = false
}: AdaptiveScreenProps) {
  const { metrics } = useAdaptiveShell();
  const outerPadding = {
    paddingHorizontal: metrics.contentHorizontalPadding
  };

  return (
    <SafeAreaView
      style={styles.safeArea}
      edges={["left", "right"]}
      {...gestureHandlers}
    >
      {scrollable ? (
        <ScrollView
          style={styles.flex}
          contentContainerStyle={[
            styles.scrollOuter,
            outerPadding,
            { paddingBottom: metrics.contentBottomInset },
            scrollContentStyle
          ]}
          showsVerticalScrollIndicator={showsVerticalScrollIndicator}
          bounces={bounces}
          refreshControl={refreshControl}
        >
          <View style={[styles.frame, { maxWidth: metrics.maxContentWidth }, contentStyle]}>
            {children}
          </View>
        </ScrollView>
      ) : (
        <View style={[styles.fixedOuter, outerPadding]}>
          <View
            style={[
              styles.frame,
              styles.fixedFrame,
              { maxWidth: metrics.maxContentWidth },
              contentStyle
            ]}
          >
            {children}
          </View>
        </View>
      )}
    </SafeAreaView>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  safeArea: {
    flex: 1,
    backgroundColor: surfaces.app
  },
  flex: {
    flex: 1
  },
  scrollOuter: {
    flexGrow: 1
  },
  fixedOuter: {
    flex: 1
  },
  frame: {
    width: "100%",
    alignSelf: "center"
  },
  fixedFrame: {
    flex: 1
  }
}));


