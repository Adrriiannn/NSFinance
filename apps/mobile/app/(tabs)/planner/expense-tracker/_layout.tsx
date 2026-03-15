import { Stack } from "expo-router";
import { ExpensePlanningProvider } from "../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { ExpenseTrackerPeriodProvider } from "../../../../src/features/expenseTracker/ExpenseTrackerPeriodContext";
import { palette } from "../../../../src/theme/tokens";

export default function ExpenseTrackerStackLayout() {
  return (
    <ExpenseTrackerPeriodProvider>
      <ExpensePlanningProvider>
        <Stack
          screenOptions={{
            headerShown: false,
            contentStyle: { backgroundColor: palette.appBackground },
            animation: "none"
          }}
        />
      </ExpensePlanningProvider>
    </ExpenseTrackerPeriodProvider>
  );
}
