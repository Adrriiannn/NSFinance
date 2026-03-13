import { Platform } from "react-native";

const azureProductionBaseUrl =
  "https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net";

type AppEnv = "development" | "preview" | "production";

const developmentFallbackBaseUrl =
  Platform.select({
    android: "http://10.0.2.2:5080",
    ios: "http://localhost:5080",
    default: "http://localhost:5080"
  }) ?? "http://localhost:5080";

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

function parseBoolean(value: string | undefined): boolean {
  return (value ?? "").trim().toLowerCase() === "true";
}

function normalizeAppEnv(value: string | undefined): AppEnv {
  const normalized = (value ?? "development").trim().toLowerCase();
  if (normalized === "production") {
    return "production";
  }

  if (normalized === "preview") {
    return "preview";
  }

  return "development";
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

function getDevelopmentApiBaseUrl(): string {
  const configuredBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL?.trim();
  const normalizedAzureBaseUrl = normalizeBaseUrl(azureProductionBaseUrl);

  if (!configuredBaseUrl) {
    return normalizeBaseUrl(developmentFallbackBaseUrl);
  }

  const normalizedConfiguredBaseUrl = normalizeBaseUrl(configuredBaseUrl);
  const allowAzureInDevelopment = parseBoolean(process.env.EXPO_PUBLIC_ALLOW_AZURE_IN_DEV);

  // In Expo dev, avoid accidental production traffic unless explicitly enabled.
  if (!allowAzureInDevelopment && normalizedConfiguredBaseUrl === normalizedAzureBaseUrl) {
    return normalizeBaseUrl(developmentFallbackBaseUrl);
  }

  return normalizedConfiguredBaseUrl;
}

function getProductionApiBaseUrl(): string {
  const normalizedAzureBaseUrl = normalizeBaseUrl(azureProductionBaseUrl);
  const configuredBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL?.trim();

  if (!configuredBaseUrl) {
    return normalizedAzureBaseUrl;
  }

  const normalizedConfiguredBaseUrl = normalizeBaseUrl(configuredBaseUrl);

  // Never allow production/preview builds to resolve to local/LAN API endpoints.
  if (isLocalLikeUrl(normalizedConfiguredBaseUrl)) {
    return normalizedAzureBaseUrl;
  }

  // Production/preview APKs are Azure-only by policy.
  if (normalizedConfiguredBaseUrl.toLowerCase() !== normalizedAzureBaseUrl.toLowerCase()) {
    return normalizedAzureBaseUrl;
  }

  return normalizedConfiguredBaseUrl;
}

function resolveApiBaseUrl(): string {
  return __DEV__ ? getDevelopmentApiBaseUrl() : getProductionApiBaseUrl();
}

const appEnv = normalizeAppEnv(process.env.EXPO_PUBLIC_APP_ENV);

export const apiConfig = {
  baseUrl: resolveApiBaseUrl(),
  timeoutMs: 12_000,
  isDebug: __DEV__,
  appEnv,
  isPreviewDiagnosticsEnabled: !__DEV__ && appEnv === "preview"
} as const;
