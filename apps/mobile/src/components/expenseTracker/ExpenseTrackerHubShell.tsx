import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { usePathname, useRouter } from "expo-router";
import { useMemo, useState, type ReactNode } from "react";
import { StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { palette } from "../../theme/tokens";
import { expenseBottomNavItems } from "../layout/bottomNavConfigs";
import { FloatingBottomNav, type FloatingBottomNavItem } from "../layout/FloatingBottomNav";
import { ModalSheet } from "../ui/surfaces/ModalSheet";
import { ListRow } from "../ui/rows/ListRow";
import { EXPENSE_HUB_CONTENT_PADDING_X } from "./expenseHubLayout";

const navItems = [
  { key: "overview", path: "/(tabs)/planner/expense-tracker/overview", matchPath: "/planner/expense-tracker/overview" },
  { key: "graphs", path: "/(tabs)/planner/expense-tracker/graphs", matchPath: "/planner/expense-tracker/graphs" },
  { key: "add", path: "/(tabs)/planner/expense-tracker/add", matchPath: "/planner/expense-tracker/add" },
  { key: "calendar", path: "/(tabs)/planner/expense-tracker/calendar", matchPath: "/planner/expense-tracker/calendar" },
  { key: "ai", path: "/companion/expense", matchPath: "/companion/expense" }
] as const;

const expenseNavMap = new Map<string, FloatingBottomNavItem>(expenseBottomNavItems.map((item) => [item.key, item]));

function buildExpenseAiCompanionHref(sourceExpenseTab: string) {
  return `/companion/expense?sourceExpenseTab=${sourceExpenseTab}` as never;
}

type ExpenseTrackerHubShellProps = {
  children: ReactNode;
};

export function ExpenseTrackerHubShell({ children }: ExpenseTrackerHubShellProps) {
  const router = useRouter();
  const pathname = usePathname();
  const [optionsSheetOpen, setOptionsSheetOpen] = useState(false);

  const currentNav = useMemo(() => {
    const normalizedPathname = pathname ?? "";
    return navItems.find((item) => normalizedPathname.startsWith(item.matchPath))?.key ?? navItems[0].key;
  }, [pathname]);

  return (
    <SafeAreaView style={styles.safeArea} edges={["left", "right"]}>
      <View style={styles.contentWrap}>{children}</View>

      <FloatingBottomNav
        items={expenseBottomNavItems}
        activeKey={currentNav}
        switcherAction={{
          label: "Finance Hub",
          icon: "wallet-outline",
          accessibilityLabel: "Return to finance tracking",
          behavior: "peek",
          autoPeekEnabled: currentNav !== "add",
          sharedRevealKey: "hub-switcher",
          onPress: () => {
            router.replace("/(tabs)" as never);
          }
        }}
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

      <ModalSheet
        visible={optionsSheetOpen}
        onClose={() => setOptionsSheetOpen(false)}
        title="Expense planning"
        subtitle="Move between plans, analytics, categories, and NS Companion."
      >
        <View style={styles.optionsList}>
          {navItems.map((item) => {
            const navMeta = expenseNavMap.get(item.key);
            if (!navMeta) {
              return null;
            }

            return (
              <ListRow
                key={item.path}
                title={navMeta.label}
                leading={
                  navMeta.iconFamily === "material" ? (
                    <MaterialCommunityIcons name={navMeta.icon as keyof typeof MaterialCommunityIcons.glyphMap} size={18} color={palette.textPrimary} />
                  ) : (
                    <Ionicons name={navMeta.icon as keyof typeof Ionicons.glyphMap} size={18} color={palette.textPrimary} />
                  )
                }
                onPress={() => {
                  setOptionsSheetOpen(false);
                  if (currentNav === item.key) {
                    return;
                  }
                  if (item.key === "ai") {
                    router.replace(buildExpenseAiCompanionHref(currentNav));
                  } else {
                    router.navigate(item.path as never);
                  }
                }}
              />
            );
          })}
          <ListRow
            title="Back to Planner"
            leading={<Ionicons name="grid-outline" size={18} color={palette.textPrimary} />}
            onPress={() => {
              setOptionsSheetOpen(false);
              router.replace("/(tabs)/planner" as never);
            }}
          />
        </View>
      </ModalSheet>
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
    paddingHorizontal: EXPENSE_HUB_CONTENT_PADDING_X
  },
  optionsList: {
    gap: 10
  }
});
