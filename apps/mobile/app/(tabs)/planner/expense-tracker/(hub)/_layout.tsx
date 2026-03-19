import { Slot, useLocalSearchParams, usePathname } from "expo-router";
import { ExpenseTrackerHubShell } from "../../../../../src/components/expenseTracker/ExpenseTrackerHubShell";

export default function ExpenseTrackerHubLayout() {
  const pathname = usePathname() ?? "";
  const params = useLocalSearchParams<{ selectionMode?: string }>();
  const selectionMode = params.selectionMode === "true";

  if (pathname.endsWith("/add") && selectionMode) {
    return <Slot />;
  }

  return (
    <ExpenseTrackerHubShell>
      <Slot />
    </ExpenseTrackerHubShell>
  );
}
