const defaultApiBaseUrl = "https://api.finance.nsireland.ie";

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

function isLocalLikeHost(host: string): boolean {
  if (!host) {
    return false;
  }

  const normalizedHost = host.trim().toLowerCase();
  if (
    normalizedHost === "localhost" ||
    normalizedHost === "127.0.0.1" ||
    normalizedHost === "10.0.2.2"
  ) {
    return true;
  }

  if (normalizedHost.startsWith("192.168.")) {
    return true;
  }

  if (normalizedHost.startsWith("10.")) {
    return true;
  }

  const parts = normalizedHost.split(".");
  if (parts.length === 4 && parts[0] === "172") {
    const secondOctet = Number(parts[1]);
    return Number.isInteger(secondOctet) && secondOctet >= 16 && secondOctet <= 31;
  }

  return false;
}

function isLocalLikeUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return isLocalLikeHost(parsed.hostname);
  } catch {
    return false;
  }
}

function resolveApiBaseUrl(): string {
  const normalizedDefaultApiBaseUrl = normalizeBaseUrl(defaultApiBaseUrl);
  const configuredBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL?.trim();

  if (!configuredBaseUrl) {
    return normalizedDefaultApiBaseUrl;
  }

  const normalizedConfiguredBaseUrl = normalizeBaseUrl(configuredBaseUrl);
  if (isLocalLikeUrl(normalizedConfiguredBaseUrl)) {
    return normalizedDefaultApiBaseUrl;
  }

  return normalizedConfiguredBaseUrl;
}

export const apiConfig = {
  baseUrl: resolveApiBaseUrl(),
  timeoutMs: 12_000
} as const;
