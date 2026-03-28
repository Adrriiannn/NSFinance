import * as ExpoLinking from "expo-linking";

export const CURRENT_CONNECT_BANK_ROUTE = "/(tabs)/accounts/connect-bank";
export const LEGACY_CONNECT_BANK_ROUTE = "/modals/add-account";
export const CONNECT_BANK_NEW_INTENT_QUERY = "intent=new";

export function buildBankConnectReturnUri() {
  return ExpoLinking.createURL(`${CURRENT_CONNECT_BANK_ROUTE}?${CONNECT_BANK_NEW_INTENT_QUERY}`);
}
