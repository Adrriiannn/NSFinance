import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { usePathname, useRouter } from "expo-router";
import { useMemo, useState, type ReactNode } from "react";
import { StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { palette } from "../../theme/tokens";
import { planningHubBottomNavItems } from "../layout/bottomNavConfigs";
import { FloatingBottomNav, type FloatingBottomNavItem } from "../layout/FloatingBottomNav";
import { ModalSheet } from "../ui/surfaces/ModalSheet";
import { ListRow } from "../ui/rows/ListRow";
import { PLANNING_HUB_CONTENT_PADDING_X } from "./planningHubLayout";
import { navigateWithProbe } from "../../lib/perf/navigationTiming";

const planningHubNavItems = [
  { key: "graphs", path: "/(tabs)/planning/analytics", matchPath: "/planning/analytics" },
  { key: "add", path: "/(tabs)/planning/categories", matchPath: "/planning/categories" },
  { key: "overview", path: "/(tabs)/planning", matchPath: "/planning" },
  { key: "calendar", path: "/(tabs)/calendar?source=planningHub&sourcePlanningHubTab=calendar", matchPath: "/calendar" },
  { key: "ai", path: "/(tabs)/companion?source=planningHub", matchPath: "/companion" }
] as const;

const planningHubNavMap = new Map<string, FloatingBottomNavItem>(planningHubBottomNavItems.map((item) => [item.key, item]));

function buildPlanningHubCompanionHref(sourcePlanningHubTab: string) {
  return `/(tabs)/companion?source=planningHub&sourcePlanningHubTab=${sourcePlanningHubTab}` as never;
}

type PlanningHubShellProps = {
  children: ReactNode;
};

export function PlanningHubShell({ children }: PlanningHubShellProps) {
  const router = useRouter();
  const pathname = usePathname();
  const [optionsSheetOpen, setOptionsSheetOpen] = useState(false);

  const currentNav = useMemo(() => {
    const normalizedPathname = pathname ?? "";
    return planningHubNavItems.find((item) => normalizedPathname.startsWith(item.matchPath))?.key ?? planningHubNavItems[0].key;
  }, [pathname]);

  return (
    <SafeAreaView style={styles.safeArea} edges={["left", "right"]}>
      <View style={styles.contentWrap}>{children}</View>

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
              "planning-bottom-nav-switcher"
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

          if (target.key === "ai") {
            navigateWithProbe(
              router as unknown as {
                push: (href: string) => void;
                replace: (href: string) => void;
                navigate?: (href: string) => void;
              },
              buildPlanningHubCompanionHref(currentNav),
              "planning-bottom-nav-item-ai"
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
            "planning-bottom-nav-item"
          );
        }}
      />

      <ModalSheet
        visible={optionsSheetOpen}
        onClose={() => setOptionsSheetOpen(false)}
        title="Planning Hub"
        subtitle="Move between plans, analytics, categories, and NS Companion."
      >
        <View style={styles.optionsList}>
          {planningHubNavItems.map((item) => {
            const navMeta = planningHubNavMap.get(item.key);
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
                    navigateWithProbe(
                      router as unknown as {
                        push: (href: string) => void;
                        replace: (href: string) => void;
                        navigate?: (href: string) => void;
                      },
                      buildPlanningHubCompanionHref(currentNav),
                      "planning-menu-item-ai"
                    );
                  } else {
                    navigateWithProbe(
                      router as unknown as {
                        push: (href: string) => void;
                        replace: (href: string) => void;
                        navigate?: (href: string) => void;
                      },
                      item.path,
                      "planning-menu-item"
                    );
                  }
                }}
              />
            );
          })}
          <ListRow
            title="Back to Cashflow"
            leading={<Ionicons name="grid-outline" size={18} color={palette.textPrimary} />}
            onPress={() => {
              setOptionsSheetOpen(false);
              navigateWithProbe(
                router as unknown as {
                  push: (href: string) => void;
                  replace: (href: string) => void;
                  navigate?: (href: string) => void;
                },
                "/(tabs)/cashflow",
                "planning-menu-back-cashflow"
              );
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
    paddingHorizontal: PLANNING_HUB_CONTENT_PADDING_X
  },
  optionsList: {
    gap: 10
  }
});

