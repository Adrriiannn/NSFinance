import { Redirect } from "expo-router";

export default function ExpenseTrackerAiRedirect() {
  return <Redirect href={"/companion/expense?sourceExpenseTab=overview" as never} />;
}
