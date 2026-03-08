const fallbackBaseUrl = "http://192.168.0.11:5080";

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

export const apiConfig = {
  baseUrl: normalizeBaseUrl(
    process.env.EXPO_PUBLIC_API_BASE_URL?.trim() || fallbackBaseUrl
  ),
  timeoutMs: 12_000,
  isDebug: __DEV__
} as const;
