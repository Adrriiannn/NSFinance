import { Redirect } from "expo-router";

export default function LegacyPlannerCompanionRedirect() {
  return <Redirect href={"/companion?source=app&sourceTab=planner" as never} />;
}
