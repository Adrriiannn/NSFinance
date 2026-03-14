import { Redirect } from "expo-router";

export default function ExpenseTrackerIndexRedirect() {
  return <Redirect href={"/(tabs)/planner/expense-tracker/overview" as never} />;
}
