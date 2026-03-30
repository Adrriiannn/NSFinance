import * as ExpoLinking from "expo-linking";

export const CURRENT_CONNECT_BANK_ROUTE = "/(tabs)/accounts/connect-bank";
export const LEGACY_CONNECT_BANK_ROUTE = "/modals/add-account";
export const CONNECT_BANK_NEW_INTENT_QUERY = "intent=new";

type BuildConnectBankRouteOptions = {
  intent?: "new";
  returnTo?: string | null;
};

type ConnectBankRouteParams = {
  intent?: string;
  returnTo?: string;
};

export function sanitizeConnectBankReturnPath(value?: string | null) {
  const normalized = value?.trim() ?? "";
  if (!normalized || !normalized.startsWith("/")) {
    return null;
  }

  if (
    normalized.startsWith(CURRENT_CONNECT_BANK_ROUTE)
    || normalized.startsWith(LEGACY_CONNECT_BANK_ROUTE)
  ) {
    return null;
  }

  return normalized;
}

export function buildConnectBankRoute(options?: BuildConnectBankRouteOptions) {
  const params: ConnectBankRouteParams = {};

  if (options?.intent === "new") {
    params.intent = "new";
  }

  const returnTo = sanitizeConnectBankReturnPath(options?.returnTo);
  if (returnTo) {
    params.returnTo = returnTo;
  }

  if (Object.keys(params).length === 0) {
    return { pathname: CURRENT_CONNECT_BANK_ROUTE as typeof CURRENT_CONNECT_BANK_ROUTE };
  }

  return {
    pathname: CURRENT_CONNECT_BANK_ROUTE as typeof CURRENT_CONNECT_BANK_ROUTE,
    params
  };
}

export function buildBankConnectReturnUri(returnTo?: string | null) {
  const normalizedReturnPath = sanitizeConnectBankReturnPath(returnTo);
  const queryParams = new URLSearchParams(CONNECT_BANK_NEW_INTENT_QUERY);
  if (normalizedReturnPath) {
    queryParams.set("returnTo", normalizedReturnPath);
  }

  return ExpoLinking.createURL(`${CURRENT_CONNECT_BANK_ROUTE}?${queryParams.toString()}`);
}
