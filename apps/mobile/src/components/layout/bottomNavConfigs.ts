import type { FloatingBottomNavItem } from "./FloatingBottomNav";

export const appBottomNavItems = [
  { key: "index", label: "Home", icon: "sparkles-outline" },
  { key: "accounts", label: "Accounts", icon: "wallet-outline" },
  { key: "activity", label: "Activity", icon: "swap-horizontal-outline" },
  { key: "planner", label: "Planner", icon: "calendar-outline" }
] as const satisfies readonly FloatingBottomNavItem[];

export const expenseBottomNavItems = [
  { key: "overview", label: "Plans", icon: "notebook-outline", iconFamily: "material" },
  { key: "graphs", label: "Analytics", icon: "pie-chart-outline" },
  { key: "add", label: "Categories", icon: "grid-outline" },
  { key: "ai", label: "NS Companion", icon: "robot-happy-outline", iconFamily: "material" }
] as const satisfies readonly FloatingBottomNavItem[];
