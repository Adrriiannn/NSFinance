import { usePathname, useRouter } from "expo-router";
import { ScrollView, View } from "react-native";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { useMemo, type ReactNode, type RefObject } from "react";
import { Ionicons } from "@expo/vector-icons";
import { GlobalAppMenu } from "../layout/GlobalAppMenu";
import { useOptionalAdaptiveShell } from "../../layout/adaptive/adaptive.hooks";
import {
  CONTENT_FRAME_HEADER_GAP,
  CONTENT_FRAME_HORIZONTAL_PADDING,
  getDockAwareContentBottomInset
} from "../../layout/contentFrame";
import { palette, createRuntimeStyleSheet } from "../../theme/tokens";
import { FloatingBottomNav } from "../layout/FloatingBottomNav";
import { planningHubBottomNavItems } from "../layout/bottomNavConfigs";
import { HeaderActionButton, HeaderShell } from "../../layout/appHeader";
import { navigateWithProbe } from "../../lib/perf/navigationTiming";

const planningHubNavItems = [
  { key: "discover", path: "/(tabs)/planning/browse", matchPath: "/planning/browse" },
  { key: "graphs", path: "/(tabs)/planning/analytics", matchPath: "/planning/analytics" },
  { key: "add", path: "/(tabs)/planning/categories", matchPath: "/planning/categories" },
  { key: "overview", path: "/(tabs)/planning", matchPath: "/planning" },
  { key: "calendar", path: "/(tabs)/calendar?source=planningHub&sourcePlanningHubTab=calendar", matchPath: "/calendar" }
] as const;

type PlanningHubScreenProps = {
  title: string;
  children: ReactNode;
  scrollViewRef?: RefObject<ScrollView | null>;
  onBackPress?: () => void;
  bottomOverlay?: ReactNode;
};

export function PlanningHubScreen({
  title,
  children,
  scrollViewRef,
  onBackPress,
  bottomOverlay
}: PlanningHubScreenProps) {
  const router = useRouter();
  const pathname = usePathname();
  const insets = useSafeAreaInsets();
  const adaptiveShell = useOptionalAdaptiveShell();

  const currentNav = useMemo(() => {
    const normalizedPathname = pathname ?? "";
    return planningHubNavItems.find((item) => normalizedPathname.startsWith(item.matchPath))?.key ?? "overview";
  }, [pathname]);

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

      <FloatingBottomNav
        items={planningHubBottomNavItems}
        activeKey={currentNav}
        switcherAction={{
          label: "Finance Hub",
          icon: "wallet-outline",
          accessibilityLabel: "Return to finance tracking",
          behavior: "peek",
          autoPeekEnabled: currentNav !== "add",
          sharedRevealKey: "hub-switcher",
          onPress: () => {
            navigateWithProbe(
              router as unknown as {
                push: (href: string) => void;
                replace: (href: string) => void;
                navigate?: (href: string) => void;
              },
              "/(tabs)",
              "planning-screen-bottom-nav-switcher"
            );
          }
        }}
        onPressItem={(item) => {
          if (currentNav === item.key) {
            return;
          }

          const target = planningHubNavItems.find((navItem) => navItem.key === item.key);
          if (!target) {
            return;
          }

          navigateWithProbe(
            router as unknown as {
              push: (href: string) => void;
              replace: (href: string) => void;
              navigate?: (href: string) => void;
            },
            target.path,
            "planning-screen-bottom-nav-item"
          );
        }}
      />
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




