import { apiRequest } from "../../lib/api/client";
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
