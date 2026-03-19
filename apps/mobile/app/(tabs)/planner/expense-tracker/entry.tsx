import { Redirect, useLocalSearchParams } from "expo-router";

export default function ExpenseTrackerEntryRedirect() {
  const params = useLocalSearchParams();

  return (
    <Redirect
      href={{
        pathname: "/planner/expense-tracker/add",
        params
      }}
    />
  );
}
