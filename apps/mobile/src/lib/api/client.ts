import { apiConfig } from "./config";
import { ApiClientError, parseApiErrorBody } from "./errors";
import { Platform } from "react-native";

type TokenResolver = () => string | null;
type UnauthorizedHandler = () => Promise<string | null>;

let tokenResolver: TokenResolver = () => null;
let unauthorizedHandler: UnauthorizedHandler | null = null;

export function setApiTokenResolver(nextResolver: TokenResolver) {
  tokenResolver = nextResolver;
}

export function setApiUnauthorizedHandler(nextHandler: UnauthorizedHandler | null) {
  unauthorizedHandler = nextHandler;
}

export function getApiAccessToken(): string | null {
  return tokenResolver();
}

function createAbortController(timeoutMs: number): {
  signal: AbortSignal;
  clear: () => void;
} {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  return {
    signal: controller.signal,
    clear: () => clearTimeout(timeout)
  };
}

async function tryParseJson(response: Response): Promise<unknown | null> {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.toLowerCase().includes("application/json")) {
    return null;
  }

  try {
    return (await response.json()) as unknown;
  } catch {
    return null;
  }
}

function withBaseUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) {
    return path;
  }

  return `${apiConfig.baseUrl}${path.startsWith("/") ? path : `/${path}`}`;
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const requestUrl = withBaseUrl(path);
  const makeRequest = async (overrideToken: string | null): Promise<Response> => {
    const headers: HeadersInit = {
      "Content-Type": "application/json",
      "x-platform": Platform.OS,
      "x-app-version": process.env.EXPO_PUBLIC_APP_VERSION || "mobile-dev",
      ...(overrideToken ? { Authorization: `Bearer ${overrideToken}` } : {}),
      ...(init?.headers ?? {})
    };

    const timeoutController = createAbortController(apiConfig.timeoutMs);
    try {
      return await fetch(requestUrl, {
        ...init,
        headers,
        signal: init?.signal ?? timeoutController.signal
      });
    } catch (error) {
      const message =
        error instanceof DOMException && error.name === "AbortError"
          ? "Request timed out. Please retry."
          : "Network request failed. Check API URL and local network connectivity.";

      throw new ApiClientError(message, 0, { details: error });
    } finally {
      timeoutController.clear();
    }
  };

  let token = tokenResolver();
  let response = await makeRequest(token);
  if (response.status === 401 && unauthorizedHandler && !requestUrl.endsWith("/api/auth/refresh")) {
    const refreshedToken = await unauthorizedHandler();
    if (refreshedToken) {
      token = refreshedToken;
      response = await makeRequest(token);
    }
  }

  if (!response.ok) {
    const parsedBody = await tryParseJson(response);
    const parsedError = parseApiErrorBody(parsedBody);
    const message =
      parsedError.message || `Request failed with status ${response.status}.`;

    if (apiConfig.isDebug) {
      console.warn("[API ERROR]", {
        url: requestUrl,
        status: response.status,
        parsedBody
      });
    }

    throw new ApiClientError(message, response.status, {
      code: parsedError.code,
      validation: parsedError.validation,
      details: parsedBody
    });
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const parsed = await tryParseJson(response);
  if (parsed === null) {
    throw new ApiClientError("Response body was empty or invalid JSON.", response.status);
  }

  return parsed as T;
}
