import type { FloatingBottomNavItem } from "./FloatingBottomNav";

export const appBottomNavItems = [
  { key: "index", label: "Home", icon: "apps-outline" },
  { key: "accounts", label: "Accounts", icon: "wallet-outline" },
  { key: "activity", label: "Activity", icon: "swap-horizontal-outline" },
  { key: "cashflow", label: "Cashflow", icon: "calendar-outline" }
] as const satisfies readonly FloatingBottomNavItem[];
