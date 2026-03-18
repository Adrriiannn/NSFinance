import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { usePathname, useRouter } from "expo-router";
import { useNavigation } from "@react-navigation/native";
import { useMemo, useState, type ReactNode } from "react";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { layout, palette, spacing, typography } from "../../theme/tokens";
import { expenseBottomNavItems } from "../layout/bottomNavConfigs";
import { FloatingBottomNav, type FloatingBottomNavItem } from "../layout/FloatingBottomNav";
import { IconButton } from "../ui/IconButton";
import { ModalSheet } from "../ui/surfaces/ModalSheet";
import { ListRow } from "../ui/rows/ListRow";

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
  title: string;
  children: ReactNode;
};

export function ExpenseTrackerHubShell({ title, children }: ExpenseTrackerHubShellProps) {
  const router = useRouter();
  const navigation = useNavigation();
  const pathname = usePathname();
  const [optionsSheetOpen, setOptionsSheetOpen] = useState(false);

  const currentNav = useMemo(() => {
    const normalizedPathname = pathname ?? "";
    return navItems.find((item) => normalizedPathname.startsWith(item.matchPath))?.key ?? navItems[0].key;
  }, [pathname]);

  const hidePageTitle = title === "Plans" || title === "Analytics" || title === "Categories";

  const handleBackPress = () => {
    if (navigation.canGoBack()) {
      navigation.goBack();
      return;
    }

    router.replace("/(tabs)/planner" as never);
  };

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right", "bottom"]}>
      <View pointerEvents="none" style={styles.backgroundGlowTop} />
      <View pointerEvents="none" style={styles.backgroundGlowBottom} />

      <View style={styles.header}>
        <View style={styles.headerTopRow}>
          <IconButton
            onPress={handleBackPress}
            icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
          />
          <View style={styles.headerActions}>
            <IconButton
              onPress={() => setOptionsSheetOpen(true)}
              accessibilityLabel="Open expense navigation"
              icon={<MaterialCommunityIcons name="menu" size={18} color={palette.textPrimary} />}
            />
          </View>
        </View>

        {!hidePageTitle ? (
          <View style={styles.headerBottomRow}>
            <Text style={styles.pageTitle}>{title}</Text>
          </View>
        ) : null}
      </View>

      <View style={styles.contentWrap}>
        <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
          {children}
        </ScrollView>
      </View>

      <FloatingBottomNav
        items={expenseBottomNavItems}
        activeKey={currentNav}
        switcherAction={{
          label: "Finance Hub",
          icon: "wallet-outline",
          accessibilityLabel: "Return to finance tracking",
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
  backgroundGlowTop: {
    position: "absolute",
    top: -90,
    right: -60,
    width: 240,
    height: 240,
    borderRadius: 120,
    backgroundColor: "rgba(47,107,255,0.11)"
  },
  backgroundGlowBottom: {
    position: "absolute",
    bottom: -140,
    left: -80,
    width: 220,
    height: 220,
    borderRadius: 110,
    backgroundColor: "rgba(111,215,255,0.06)"
  },
  header: {
    paddingHorizontal: layout.screenHorizontalPadding,
    paddingTop: spacing[8],
    gap: spacing[12]
  },
  headerTopRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  headerBottomRow: {
    gap: spacing[4]
  },
  headerActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  pageTitle: {
    color: palette.textPrimary,
    ...typography.title1
  },
  contentWrap: {
    flex: 1,
    marginTop: spacing[12]
  },
  content: {
    paddingHorizontal: layout.screenHorizontalPadding,
    paddingBottom: 120,
    gap: layout.sectionGap
  },
  optionsList: {
    gap: 10
  }
});
