import { Ionicons } from "@expo/vector-icons";
import { type ReactNode, type RefObject } from "react";
import { ScrollView, View } from "react-native";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { HeaderActionButton, HeaderShell } from "../../layout/appHeader";
import { useOptionalAdaptiveShell } from "../../layout/adaptive/adaptive.hooks";
import {
  CONTENT_FRAME_HEADER_GAP,
  CONTENT_FRAME_HORIZONTAL_PADDING,
  getDockAwareContentBottomInset
} from "../../layout/contentFrame";
import { palette, createRuntimeStyleSheet, useThemeTokens } from "../../theme/tokens";
import { GlobalAppMenu } from "../layout/GlobalAppMenu";

type CategoryPickerScreenProps = {
  title: string;
  children: ReactNode;
  scrollViewRef?: RefObject<ScrollView | null>;
  onBackPress?: () => void;
  bottomOverlay?: ReactNode;
};

export function CategoryPickerScreen({
  title,
  children,
  scrollViewRef,
  onBackPress,
  bottomOverlay
}: CategoryPickerScreenProps) {
  useThemeTokens();
  const insets = useSafeAreaInsets();
  const adaptiveShell = useOptionalAdaptiveShell();

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right"]}>
      {!adaptiveShell ? (
        <GlobalAppMenu topOffset={insets.top + CONTENT_FRAME_HEADER_GAP} showTrigger={false} />
      ) : null}

      <HeaderShell
        preset="secondaryDetail"
        title={title}
        bleedHorizontal={0}
        leadingAction={
          onBackPress ? (
            <HeaderActionButton
              icon={<Ionicons name="arrow-back" size={20} color={palette.textPrimary} />}
              accessibilityLabel="Go back"
              onPress={onBackPress}
            />
          ) : undefined
        }
      />

      <View style={styles.contentWrap}>
        <ScrollView
          ref={scrollViewRef}
          contentContainerStyle={[
            styles.content,
            { paddingBottom: getDockAwareContentBottomInset(insets.bottom) }
          ]}
          showsVerticalScrollIndicator={false}
        >
          {children}
        </ScrollView>
      </View>

      {bottomOverlay}
    </SafeAreaView>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  safeArea: {
    flex: 1,
    backgroundColor: palette.appBackground
  },
  contentWrap: {
    flex: 1,
    marginTop: CONTENT_FRAME_HEADER_GAP
  },
  content: {
    paddingHorizontal: CONTENT_FRAME_HORIZONTAL_PADDING,
    gap: 20
  }
}));
