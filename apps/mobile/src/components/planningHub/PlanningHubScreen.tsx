import { usePathname, useRouter } from "expo-router";
import { ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { useMemo, type ReactNode, type RefObject } from "react";
import { GlobalAppMenu } from "../layout/GlobalAppMenu";
import { useOptionalAdaptiveShell } from "../../layout/adaptive/adaptive.hooks";
import {
  CONTENT_FRAME_HEADER_GAP,
  CONTENT_FRAME_HORIZONTAL_PADDING,
  getDockAwareContentBottomInset
} from "../../layout/contentFrame";
import { palette } from "../../theme/tokens";
import { FloatingBottomNav } from "../layout/FloatingBottomNav";
import { planningHubBottomNavItems } from "../layout/bottomNavConfigs";
import { HeaderShell } from "../../layout/appHeader";
import { navigateWithProbe } from "../../lib/perf/navigationTiming";

const planningHubNavItems = [
  { key: "graphs", path: "/(tabs)/planning/analytics", matchPath: "/planning/analytics" },
  { key: "add", path: "/(tabs)/planning/categories", matchPath: "/planning/categories" },
  { key: "overview", path: "/(tabs)/planning", matchPath: "/planning" },
  { key: "calendar", path: "/(tabs)/calendar?source=planningHub&sourcePlanningHubTab=calendar", matchPath: "/calendar" },
  { key: "ai", path: "/(tabs)/companion?source=planningHub", matchPath: "/companion" }
] as const;

function buildPlanningHubCompanionHref(sourcePlanningHubTab: string) {
  return `/(tabs)/companion?source=planningHub&sourcePlanningHubTab=${sourcePlanningHubTab}` as never;
}

type PlanningHubScreenProps = {
  title: string;
  children: ReactNode;
  scrollViewRef?: RefObject<ScrollView | null>;
};

export function PlanningHubScreen({ title, children, scrollViewRef }: PlanningHubScreenProps) {
  const router = useRouter();
  const pathname = usePathname();
  const insets = useSafeAreaInsets();
  const adaptiveShell = useOptionalAdaptiveShell();

  const currentNav = useMemo(() => {
    const normalizedPathname = pathname ?? "";
    return planningHubNavItems.find((item) => normalizedPathname.startsWith(item.matchPath))?.key ?? planningHubNavItems[0].key;
  }, [pathname]);

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right"]}>
      {!adaptiveShell ? (
        <GlobalAppMenu topOffset={insets.top + CONTENT_FRAME_HEADER_GAP} showTrigger={false} />
      ) : null}

      <HeaderShell preset="secondaryDetail" title={title} />

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

      <FloatingBottomNav
        items={planningHubBottomNavItems}
        activeKey={currentNav}
        onPressItem={(item) => {
          if (currentNav === item.key) {
            return;
          }

          const target = planningHubNavItems.find((navItem) => navItem.key === item.key);
          if (!target) {
            return;
          }

          if (target.key === "ai") {
            navigateWithProbe(
              router as unknown as {
                push: (href: string) => void;
                replace: (href: string) => void;
                navigate?: (href: string) => void;
              },
              buildPlanningHubCompanionHref(currentNav),
              "planning-screen-bottom-nav-ai"
            );
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

const styles = StyleSheet.create({
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
});


