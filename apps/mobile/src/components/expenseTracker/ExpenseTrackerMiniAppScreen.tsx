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
import { expenseBottomNavItems } from "../layout/bottomNavConfigs";
import { HeaderShell } from "../../layout/appHeader";

const navItems = [
  { key: "overview", path: "/(tabs)/planner/expense-tracker/overview", matchPath: "/planner/expense-tracker/overview" },
  { key: "graphs", path: "/(tabs)/planner/expense-tracker/graphs", matchPath: "/planner/expense-tracker/graphs" },
  { key: "add", path: "/(tabs)/planner/expense-tracker/add", matchPath: "/planner/expense-tracker/add" },
  { key: "calendar", path: "/(tabs)/planner/expense-tracker/calendar", matchPath: "/planner/expense-tracker/calendar" },
  { key: "ai", path: "/companion/expense", matchPath: "/companion/expense" }
] as const;

function buildExpenseAiCompanionHref(sourceExpenseTab: string) {
  return `/companion/expense?sourceExpenseTab=${sourceExpenseTab}` as never;
}

type ExpenseTrackerMiniAppScreenProps = {
  title: string;
  children: ReactNode;
  scrollViewRef?: RefObject<ScrollView | null>;
};

export function ExpenseTrackerMiniAppScreen({ title, children, scrollViewRef }: ExpenseTrackerMiniAppScreenProps) {
  const router = useRouter();
  const pathname = usePathname();
  const insets = useSafeAreaInsets();
  const adaptiveShell = useOptionalAdaptiveShell();

  const currentNav = useMemo(() => {
    const normalizedPathname = pathname ?? "";
    return navItems.find((item) => normalizedPathname.startsWith(item.matchPath))?.key ?? navItems[0].key;
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
        items={expenseBottomNavItems}
        activeKey={currentNav}
        onPressItem={(item) => {
          if (currentNav === item.key) {
            return;
          }

          const target = navItems.find((navItem) => navItem.key === item.key);
          if (!target) {
            return;
          }

          if (target.key === "ai") {
            router.replace(buildExpenseAiCompanionHref(currentNav));
            return;
          }

          router.navigate(target.path as never);
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

