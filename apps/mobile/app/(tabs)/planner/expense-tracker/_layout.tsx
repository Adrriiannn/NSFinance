import { Stack } from "expo-router";
import { ExpenseTrackerPeriodProvider } from "../../../../src/features/expenseTracker/ExpenseTrackerPeriodContext";
import { palette } from "../../../../src/theme/tokens";

export default function ExpenseTrackerStackLayout() {
  return (
    <ExpenseTrackerPeriodProvider>
      <Stack
        screenOptions={{
          headerShown: false,
          contentStyle: { backgroundColor: palette.appBackground },
          animation: "none"
        }}
      />
    </ExpenseTrackerPeriodProvider>
  );
}
