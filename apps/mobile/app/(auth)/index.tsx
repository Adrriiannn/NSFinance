import { Redirect } from "expo-router";

export default function AuthEntryScreen() {
  return <Redirect href="/(auth)/login" />;
}
