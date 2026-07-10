import runtimeConfig from "../../../runtime.config.json";

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

export const apiConfig = {
  baseUrl: normalizeBaseUrl(runtimeConfig.apiBaseUrl),
  timeoutMs: 12_000
} as const;
