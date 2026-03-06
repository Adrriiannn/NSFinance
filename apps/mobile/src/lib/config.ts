const fallbackApiBaseUrl = "http://192.168.0.11:5080";

export const apiBaseUrl =
  process.env.EXPO_PUBLIC_API_BASE_URL?.trim() || fallbackApiBaseUrl;
