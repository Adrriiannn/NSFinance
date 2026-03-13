import { apiConfig } from "./config";

const registerPath = "/api/auth/register";
const loginPath = "/api/auth/login";

export function resolveApiRequestUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) {
    return path;
  }

  return `${apiConfig.baseUrl}${path.startsWith("/") ? path : `/${path}`}`;
}

export const authApiRouteDiagnostics = {
  enabled: apiConfig.isPreviewDiagnosticsEnabled,
  appEnv: apiConfig.appEnv,
  baseUrl: apiConfig.baseUrl,
  registerPath,
  loginPath,
  registerUrl: resolveApiRequestUrl(registerPath),
  loginUrl: resolveApiRequestUrl(loginPath)
} as const;

export function getAuthApiDebugDetail(): string | undefined {
  if (!authApiRouteDiagnostics.enabled) {
    return undefined;
  }

  return [
    `Env: ${authApiRouteDiagnostics.appEnv}`,
    `API base: ${authApiRouteDiagnostics.baseUrl}`,
    `Register URL: ${authApiRouteDiagnostics.registerUrl}`,
    `Login URL: ${authApiRouteDiagnostics.loginUrl}`
  ].join("\n");
}
