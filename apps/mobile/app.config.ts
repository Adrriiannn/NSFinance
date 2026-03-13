import type { ConfigContext, ExpoConfig } from "expo/config";

const appJson = require("./app.json") as { expo: ExpoConfig };

const azureProductionBaseUrl =
  "https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net";
const androidPackageName = "com.nsfinance.mobile";

function normalizeAppEnv(value: string | undefined): "development" | "preview" | "production" {
  const normalized = (value ?? "development").trim().toLowerCase();

  if (normalized === "production") {
    return "production";
  }

  if (normalized === "preview") {
    return "preview";
  }

  return "development";
}

export default ({ config }: ConfigContext): ExpoConfig => {
  const appEnv = normalizeAppEnv(process.env.EXPO_PUBLIC_APP_ENV);
  const baseConfig = appJson.expo;

  const mergedConfig: ExpoConfig = {
    ...baseConfig,
    ...config,
    name: appEnv === "development" ? "NSFinance Dev" : "NSFinance",
    slug: baseConfig.slug,
    version: process.env.EXPO_PUBLIC_APP_VERSION?.trim() || baseConfig.version || "1.0.0",
    android: {
      ...(baseConfig.android ?? {}),
      ...(config.android ?? {}),
      package: androidPackageName
    },
    extra: {
      ...(baseConfig.extra ?? {}),
      ...(config.extra ?? {}),
      appEnv,
      defaultProductionApiBaseUrl: azureProductionBaseUrl,

      eas: {
        projectId: "21986a2d-cbfa-4757-bf6d-04eb6aa4f197"
      }
    }
  };

  return mergedConfig;
};