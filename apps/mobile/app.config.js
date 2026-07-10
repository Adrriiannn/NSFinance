const appJson = require("./app.json");

const defaultApiBaseUrl = "https://api.finance.nsireland.ie";
const androidPackageName = "com.nsfinance.mobile";
const appSchemes = ["nsfinance", androidPackageName];

module.exports = ({ config }) => {
  const baseConfig = appJson.expo;

  return {
    ...baseConfig,
    ...config,
    name: "NSFinance",
    slug: baseConfig.slug,
    scheme: appSchemes,
    version: process.env.EXPO_PUBLIC_APP_VERSION?.trim() || baseConfig.version || "1.0.0",
    android: {
      ...(baseConfig.android ?? {}),
      ...(config.android ?? {}),
      package: androidPackageName
    },
    extra: {
      ...(baseConfig.extra ?? {}),
      ...(config.extra ?? {}),
      defaultApiBaseUrl,
      eas: {
        projectId: "21986a2d-cbfa-4757-bf6d-04eb6aa4f197"
      }
    }
  };
};
