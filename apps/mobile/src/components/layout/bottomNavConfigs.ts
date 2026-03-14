import type { FloatingBottomNavItem } from "./FloatingBottomNav";

export const appBottomNavItems = [
  { key: "index", label: "Home", icon: "sparkles-outline" },
  { key: "accounts", label: "Accounts", icon: "wallet-outline" },
  { key: "activity", label: "Activity", icon: "swap-horizontal-outline" },
  { key: "planner", label: "Planner", icon: "calendar-outline" }
] as const satisfies readonly FloatingBottomNavItem[];

export const expenseBottomNavItems = [
  { key: "overview", label: "Overview", icon: "home-outline" },
  { key: "graphs", label: "Graphs", icon: "pie-chart-outline" },
  { key: "add", label: "Add Expense", icon: "add-circle-outline" },
  { key: "ai", label: "NSF AI", icon: "sparkles-outline" }
] as const satisfies readonly FloatingBottomNavItem[];
