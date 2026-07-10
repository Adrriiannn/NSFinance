import { apiRequest, getApiAccessToken } from "../../lib/api/client";
import { apiConfig } from "../../lib/api/config";
import { ApiClientError, parseApiErrorBody } from "../../lib/api/errors";
import { appMetadata } from "../../lib/config/appMetadata";
import * as FileSystem from "expo-file-system/legacy";
import { NativeModules, Platform } from "react-native";
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

export type ExportDownloadResult = {
  uri: string;
  usedAndroidDownloadManager: boolean;
};

export async function downloadExportRequestFile(requestId: string): Promise<ExportDownloadResult> {
  const accessToken = getApiAccessToken();
  if (!accessToken) {
    throw new Error("You need to be signed in to download exports.");
  }

  const url = `${apiConfig.baseUrl}/api/support/export-requests/${requestId}/download`;
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    "x-platform": Platform.OS,
    "x-app-version": appMetadata.version
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

  const requestedExtension = "xlsx";
  const mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
  const exportFileName = `nsfinance-export-${requestId}-${new Date().toISOString().replace(/[:.]/g, "-")}.${requestedExtension}`;
  const turboModuleProxy = (globalThis as { __turboModuleProxy?: ((name: string) => unknown) | null }).__turboModuleProxy;
  const hasTurboBlobUtil =
    typeof turboModuleProxy === "function" && Boolean(turboModuleProxy("ReactNativeBlobUtil"));
  const hasLegacyBlobUtil = Boolean((NativeModules as Record<string, unknown>).ReactNativeBlobUtil);
  const canUseNativeAndroidDownloadManager =
    Platform.OS === "android" && (hasLegacyBlobUtil || hasTurboBlobUtil);

  if (canUseNativeAndroidDownloadManager) {
    try {
      const blobUtilModule = await import("react-native-blob-util");
      const RNBlobUtil = blobUtilModule.default;
      const downloadPath = `${RNBlobUtil.fs.dirs.DownloadDir}/${exportFileName}`;
      const downloadResult = await RNBlobUtil
        .config({
          fileCache: false,
          path: downloadPath,
          addAndroidDownloads: {
            useDownloadManager: true,
            notification: true,
            mediaScannable: true,
            title: exportFileName,
            description: "NSFinance statements export",
            mime: mimeType,
            path: downloadPath
          }
        })
        .fetch("GET", url, headers);

      const statusCode = Number(downloadResult.info().status ?? 200);
      if (statusCode >= 400) {
        throw new ApiClientError(`Export download failed with status ${statusCode}.`, statusCode);
      }

      const path = downloadResult.path();
      return {
        uri: path.startsWith("file://") ? path : `file://${path}`,
        usedAndroidDownloadManager: true
      };
    } catch {
      // Fallback path for environments where native Download Manager integration is unavailable.
    }
  }

  const destinationPath = `${FileSystem.cacheDirectory ?? FileSystem.documentDirectory}nsfinance-export-${requestId}.${requestedExtension}`;

  const result = await FileSystem.downloadAsync(url, destinationPath, {
    headers
  });

  if ((result.status ?? 200) >= 400) {
    throw new ApiClientError(`Export download failed with status ${result.status}.`, result.status ?? 500);
  }

  return {
    uri: result.uri,
    usedAndroidDownloadManager: false
  };
}
