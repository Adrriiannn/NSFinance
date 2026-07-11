const appJson = require("./app.json");
const runtimeConfig = require("./runtime.config.json");

const androidPackageName = "com.nsfinance.mobile";

module.exports = ({ config }) => {
  const baseConfig = appJson.expo;

  return {
    ...baseConfig,
    ...config,
    name: "NSFinance",
    slug: baseConfig.slug,
    scheme: baseConfig.scheme,
    version: baseConfig.version,
    android: {
      ...(baseConfig.android ?? {}),
      ...(config.android ?? {}),
      package: androidPackageName
    },
    extra: {
      ...(baseConfig.extra ?? {}),
      ...(config.extra ?? {}),
      runtime: runtimeConfig,
      eas: {
        projectId: "21986a2d-cbfa-4757-bf6d-04eb6aa4f197"
      }
    }
  };
};
