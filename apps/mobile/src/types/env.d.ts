declare namespace NodeJS {
  interface ProcessEnv {
    EXPO_PUBLIC_API_BASE_URL?: string;
    EXPO_PUBLIC_TURNSTILE_PAGE_BASE_URL?: string;
    EXPO_PUBLIC_APP_ENV?: "development" | "preview" | "production";
    EXPO_PUBLIC_ALLOW_AZURE_IN_DEV?: "true" | "false";
    EXPO_PUBLIC_APP_VERSION?: string;
    EXPO_PUBLIC_GOOGLE_CLIENT_ID?: string;
    EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID?: string;
    EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID?: string;
    EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID?: string;
    EXPO_PUBLIC_NSFINANCE_WEBSITE_URL?: string;
    EXPO_PUBLIC_NSFINANCE_INSTAGRAM_URL?: string;
  }
}
