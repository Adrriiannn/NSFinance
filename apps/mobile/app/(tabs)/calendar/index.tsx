import { Redirect } from "expo-router";

export default function LegacyCalendarRedirect() {
  return <Redirect href="/(tabs)/cashflow" />;
}
