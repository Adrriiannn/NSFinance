import { Redirect, useLocalSearchParams } from "expo-router";
import { buildConnectBankRoute } from "../../src/features/banking/bankingLinking";

export default function LegacyAddAccountRedirect() {
  const params = useLocalSearchParams<{ returnTo?: string }>();

  return (
    <Redirect
      href={buildConnectBankRoute({
        intent: "new",
        returnTo: typeof params.returnTo === "string" ? params.returnTo : null
      }) as never}
    />
  );
}
