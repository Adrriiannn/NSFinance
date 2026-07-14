import { Redirect } from "expo-router";

export default function LegacyTransferRedirect() {
  return <Redirect href="/(tabs)/accounts" />;
}
