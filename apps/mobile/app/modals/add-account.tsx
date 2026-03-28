import { Redirect, useLocalSearchParams } from "expo-router";
import { CURRENT_CONNECT_BANK_ROUTE } from "../../src/features/banking/bankingLinking";

function toQueryString(params: Record<string, string | string[]>) {
  const searchParams = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (Array.isArray(value)) {
      for (const entry of value) {
        searchParams.append(key, entry);
      }
      continue;
    }

    searchParams.set(key, value);
  }

  const query = searchParams.toString();
  return query.length > 0 ? `?${query}` : "";
}

export default function LegacyAddAccountRedirect() {
  const params = useLocalSearchParams<Record<string, string | string[]>>();
  const queryString = toQueryString(params);

  return <Redirect href={`${CURRENT_CONNECT_BANK_ROUTE}${queryString}` as never} />;
}
