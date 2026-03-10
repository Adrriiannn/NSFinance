import { NativeModules, Platform } from "react-native";

const productionBaseUrl =
  "https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net";

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

function extractRuntimeHost(): string | null {
  const scriptUrl = (NativeModules as { SourceCode?: { scriptURL?: string } }).SourceCode?.scriptURL;

  if (!scriptUrl) {
    return null;
  }

  try {
    const parsed = new URL(scriptUrl);
    return parsed.hostname || null;
  } catch {
    return null;
  }
}

function getDevelopmentBaseUrl(): string {
  const runtimeHost = extractRuntimeHost();

  if (runtimeHost && runtimeHost !== "localhost" && runtimeHost !== "127.0.0.1") {
    return `http://${runtimeHost}:5080`;
  }

  return (
    Platform.select({
      android: "http://10.0.2.2:5080",
      ios: "http://localhost:5080",
      default: "http://localhost:5080"
    }) ?? "http://localhost:5080"
  );
}

function resolveApiBaseUrl(): string {
  const configuredBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL?.trim();
  const normalizedProductionBaseUrl = normalizeBaseUrl(productionBaseUrl);
  const normalizedDevelopmentBaseUrl = normalizeBaseUrl(getDevelopmentBaseUrl());

  if (!configuredBaseUrl) {
    return __DEV__ ? normalizedDevelopmentBaseUrl : normalizedProductionBaseUrl;
  }

  const normalizedConfiguredBaseUrl = normalizeBaseUrl(configuredBaseUrl);

  // Avoid hitting production API accidentally during local Expo development.
  if (__DEV__ && normalizedConfiguredBaseUrl === normalizedProductionBaseUrl) {
    return normalizedDevelopmentBaseUrl;
  }

  return normalizedConfiguredBaseUrl;
}

export const apiConfig = {
  baseUrl: resolveApiBaseUrl(),
  timeoutMs: 12_000,
  isDebug: __DEV__
} as const;