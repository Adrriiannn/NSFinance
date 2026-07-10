import appConfig from "../../../app.json";

export const appMetadata = {
  version: appConfig.expo.version,
  androidVersionCode: appConfig.expo.android.versionCode,
  runtimeVersion: appConfig.expo.android.runtimeVersion
} as const;
