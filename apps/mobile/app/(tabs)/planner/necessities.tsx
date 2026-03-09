import { Redirect } from "expo-router";

export default function PlannerNecessitiesLegacyRoute() {
  return <Redirect href={"/(tabs)/planner/upcoming-payments" as never} />;
}
