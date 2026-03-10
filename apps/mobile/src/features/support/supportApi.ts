import { apiRequest, getApiAccessToken } from "../../lib/api/client";
import { apiConfig } from "../../lib/api/config";
import { ApiClientError, parseApiErrorBody } from "../../lib/api/errors";
import * as FileSystem from "expo-file-system/legacy";
import { Platform } from "react-native";
import type {
  CreateDeletionRequestRequest,
  CreateExportRequestRequest,
  CreateSupportRequestRequest,
  DeletionRequestDto,
  ExportRequestDto,
  SupportRequestDto
} from "../../types/api";

export function createSupportRequest(
  payload: CreateSupportRequestRequest
): Promise<SupportRequestDto> {
  return apiRequest<SupportRequestDto>("/api/support/requests", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function getMySupportRequests(): Promise<SupportRequestDto[]> {
  return apiRequest<SupportRequestDto[]>("/api/support/requests/me");
}

export function createDeletionRequest(
  payload: CreateDeletionRequestRequest
): Promise<DeletionRequestDto> {
  return apiRequest<DeletionRequestDto>("/api/support/deletion-requests", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function createExportRequest(
  payload: CreateExportRequestRequest
): Promise<ExportRequestDto> {
  return apiRequest<ExportRequestDto>("/api/support/export-requests", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function getMyExportRequests(): Promise<ExportRequestDto[]> {
  return apiRequest<ExportRequestDto[]>("/api/support/export-requests/me");
}

export function getMyDeletionRequests(): Promise<DeletionRequestDto[]> {
  return apiRequest<DeletionRequestDto[]>("/api/support/deletion-requests/me");
}

export async function downloadExportRequestFile(requestId: string): Promise<string> {
  const accessToken = getApiAccessToken();
  if (!accessToken) {
    throw new Error("You need to be signed in to download exports.");
  }

  const url = `${apiConfig.baseUrl}/api/support/export-requests/${requestId}/download`;
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    "x-platform": Platform.OS,
    "x-app-version": process.env.EXPO_PUBLIC_APP_VERSION || "mobile-dev"
  };

  const preflight = await fetch(url, {
    method: "GET",
    headers
  });

  if (!preflight.ok) {
    const contentType = preflight.headers.get("content-type") ?? "";
    let parsedBody: unknown = null;
    if (contentType.toLowerCase().includes("application/json")) {
      try {
        parsedBody = await preflight.json();
      } catch {
        parsedBody = null;
      }
    }

    const parsedError = parseApiErrorBody(parsedBody);
    throw new ApiClientError(
      parsedError.message || `Export download failed with status ${preflight.status}.`,
      preflight.status,
      {
        code: parsedError.code,
        validation: parsedError.validation,
        details: parsedBody
      }
    );
  }

  const destinationPath = `${FileSystem.cacheDirectory ?? FileSystem.documentDirectory}nsfinance-export-${requestId}.json`;

  const result = await FileSystem.downloadAsync(url, destinationPath, {
    headers
  });

  if ((result.status ?? 200) >= 400) {
    throw new ApiClientError(`Export download failed with status ${result.status}.`, result.status ?? 500);
  }

  return result.uri;
}
