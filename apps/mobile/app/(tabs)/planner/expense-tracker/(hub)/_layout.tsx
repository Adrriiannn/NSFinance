import { Slot, useLocalSearchParams, usePathname } from "expo-router";
import { ExpenseTrackerHubShell } from "../../../../../src/components/expenseTracker/ExpenseTrackerHubShell";

function resolveHubTitle(pathname: string, selectionMode: boolean) {
  if (pathname.endsWith("/overview")) {
    return "Plans";
  }

  if (pathname.endsWith("/graphs")) {
    return "Analytics";
  }

  if (pathname.endsWith("/calendar")) {
    return "Calendar";
  }

  if (pathname.endsWith("/add")) {
    return selectionMode ? "Select category" : "Categories";
  }

  return "Plans";
}

export default function ExpenseTrackerHubLayout() {
  const pathname = usePathname() ?? "";
  const params = useLocalSearchParams<{ selectionMode?: string }>();
  const selectionMode = params.selectionMode === "true";

  if (pathname.endsWith("/add") && selectionMode) {
    return <Slot />;
  }

  return (
    <ExpenseTrackerHubShell title={resolveHubTitle(pathname, selectionMode)}>
      <Slot />
    </ExpenseTrackerHubShell>
  );
}
