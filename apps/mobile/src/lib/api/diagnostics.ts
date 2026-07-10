import { apiConfig } from "./config";

export function resolveApiRequestUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) {
    return path;
  }

  return `${apiConfig.baseUrl}${path.startsWith("/") ? path : `/${path}`}`;
}
