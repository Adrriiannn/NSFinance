import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { usePathname, useRouter } from "expo-router";
import { Modal, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { useNavigation } from "@react-navigation/native";
import { useMemo, useState, type ReactNode, type RefObject } from "react";
import { useExpenseTrackerPeriod } from "../../features/expenseTracker/ExpenseTrackerPeriodContext";
import { layout, palette, radius, spacing, typography } from "../../theme/tokens";
import { FloatingBottomNav } from "../layout/FloatingBottomNav";
import { IconButton } from "../ui/IconButton";
import { expenseBottomNavItems } from "../layout/bottomNavConfigs";

const navItems = [
  { key: "overview", path: "/(tabs)/planner/expense-tracker/overview", matchPath: "/planner/expense-tracker/overview" },
  { key: "graphs", path: "/(tabs)/planner/expense-tracker/graphs", matchPath: "/planner/expense-tracker/graphs" },
  { key: "add", path: "/(tabs)/planner/expense-tracker/add", matchPath: "/planner/expense-tracker/add" },
  { key: "ai", path: "/companion/expense", matchPath: "/companion/expense" }
] as const;

const expenseNavMap = new Map(expenseBottomNavItems.map((item) => [item.key, item]));

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
  const navigation = useNavigation();
  const pathname = usePathname();
  const { mode, setMode, period } = useExpenseTrackerPeriod();
  const [optionsSheetOpen, setOptionsSheetOpen] = useState(false);

  const currentNav = useMemo(() => {
    const normalizedPathname = pathname ?? "";
    return navItems.find((item) => normalizedPathname.startsWith(item.matchPath))?.key ?? navItems[0].key;
  }, [pathname]);

  const hidePageTitle = title === "Overview" || title === "Graphs" || title === "Add Expense";
  const periodHeadline = mode === "weekly" ? "Weekly" : "Monthly";
  const periodSubheadline = mode === "weekly"
    ? period.label
    : period.end.toLocaleDateString("en-GB", { month: "short", year: "numeric" });

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
            <Pressable
              style={styles.periodButton}
              onPress={() => setMode(mode === "weekly" ? "monthly" : "weekly")}
            >
              <View style={styles.periodButtonIconWrap}>
                <MaterialCommunityIcons name="calendar-range-outline" size={18} color={palette.textPrimary} />
              </View>
              <View style={styles.periodButtonTextWrap}>
                <Text style={styles.periodButtonLabel}>{periodHeadline}</Text>
                <Text style={styles.periodButtonValue}>{periodSubheadline}</Text>
              </View>
            </Pressable>
            <Pressable style={styles.iconButton} onPress={() => setOptionsSheetOpen(true)}>
              <MaterialCommunityIcons name="menu" size={18} color={palette.textPrimary} />
            </Pressable>
          </View>
        </View>

        {!hidePageTitle ? (
          <View style={styles.headerBottomRow}>
            <View>
              <Text style={styles.pageTitle}>{title}</Text>
            </View>
          </View>
        ) : null}
      </View>

      <View style={styles.contentWrap}>
        <ScrollView ref={scrollViewRef} contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
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

          const target = navItems.find((navItem) => navItem.key == item.key);
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

      <Modal visible={optionsSheetOpen} transparent animationType="fade" onRequestClose={() => setOptionsSheetOpen(false)}>
        <Pressable style={styles.overlay} onPress={() => setOptionsSheetOpen(false)}>
          <Pressable style={styles.sheet} onPress={() => undefined}>
            <Text style={styles.sheetTitle}>Expense options</Text>
            <Text style={styles.sheetSubtitle}>Quick ways to move around your manual expense space.</Text>
            <View style={styles.optionsList}>
              {navItems.map((item) => {
                const navMeta = expenseNavMap.get(item.key);
                if (!navMeta) {
                  return null;
                }

                return (
                  <Pressable
                    key={item.path}
                    style={styles.optionRow}
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
                  >
                    <Ionicons name={navMeta.icon} size={18} color={palette.textPrimary} />
                    <Text style={styles.optionLabel}>{navMeta.label}</Text>
                  </Pressable>
                );
              })}
              <Pressable
                style={styles.optionRow}
                onPress={() => {
                  setOptionsSheetOpen(false);
                  router.replace("/(tabs)/planner" as never);
                }}
              >
                <Ionicons name="grid-outline" size={18} color={palette.textPrimary} />
                <Text style={styles.optionLabel}>Back to Planner</Text>
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </Modal>
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
  periodButton: {
    minHeight: 42,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    paddingLeft: 8,
    paddingRight: 12,
    flexDirection: "row",
    alignItems: "center",
    gap: 10
  },
  periodButtonIconWrap: {
    width: 24,
    height: 24,
    borderRadius: 8,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(47,107,255,0.22)"
  },
  periodButtonTextWrap: {
    gap: 2,
    alignItems: "flex-end"
  },
  periodButtonLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700",
    lineHeight: 14,
    textAlign: "right"
  },
  periodButtonValue: {
    color: palette.textSecondary,
    ...typography.caption,
    lineHeight: 14,
    textAlign: "right"
  },
  iconButton: {
    width: 42,
    height: 42,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)"
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
  overlay: {
    flex: 1,
    justifyContent: "flex-end",
    backgroundColor: "rgba(4,11,23,0.72)"
  },
  sheet: {
    borderTopLeftRadius: radius.hero,
    borderTopRightRadius: radius.hero,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    paddingHorizontal: spacing[20],
    paddingTop: spacing[20],
    paddingBottom: spacing[24],
    gap: spacing[16]
  },
  sheetTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  sheetSubtitle: {
    color: palette.textSecondary,
    ...typography.body2
  },
  optionsList: {
    gap: 10
  },
  optionRow: {
    minHeight: 50,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    paddingHorizontal: spacing[16]
  },
  optionLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  }
});
