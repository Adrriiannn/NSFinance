import type { FloatingBottomNavItem } from "./FloatingBottomNav";

export const appBottomNavItems = [
  { key: "index", label: "Home", icon: "apps-outline" },
  { key: "accounts", label: "Accounts", icon: "wallet-outline" },
  { key: "activity", label: "Activity", icon: "swap-horizontal-outline" },
  { key: "cashflow", label: "Cashflow", icon: "calendar-outline" },
  { key: "calendar", label: "Calendar", icon: "today-outline" }
] as const satisfies readonly FloatingBottomNavItem[];

export const planningHubBottomNavItems = [
  { key: "overview", label: "Plans", icon: "notebook-outline", iconFamily: "material" },
  { key: "graphs", label: "Analytics", icon: "pie-chart-outline" },
  { key: "add", label: "Categories", icon: "grid-outline" },
  { key: "discover", label: "Discover", icon: "compass-outline" },
  { key: "calendar", label: "Calendar", icon: "today-outline" }
] as const satisfies readonly FloatingBottomNavItem[];
