import { apiRequest } from "../../lib/api/client";
import type {
  ReviewStatementImportRowsRequest,
  StatementCsvInspectionDto,
  StatementImportBatchDto,
  StatementImportLifecycleMutationDto,
  StatementImportMappingRequest,
  StatementImportPreviewDto,
  StatementImportReviewMutationDto,
  StatementImportRowsRequest
} from "../../types/api";

export type StatementDocumentAsset = {
  uri: string;
  name: string;
  mimeType?: string | null;
};

function appendDocument(formData: FormData, asset: StatementDocumentAsset) {
  formData.append(
    "file",
    {
      uri: asset.uri,
      name: asset.name,
      type: asset.mimeType || "text/csv"
    } as unknown as Blob
  );
}

function appendOptionalField(
  formData: FormData,
  name: string,
  value: string | number | null | undefined
) {
  if (value !== null && value !== undefined && value !== "") {
    formData.append(name, String(value));
  }
}

export function inspectStatementCsv(
  asset: StatementDocumentAsset,
  delimiter?: string | null
): Promise<StatementCsvInspectionDto> {
  const formData = new FormData();
  appendDocument(formData, asset);
  appendOptionalField(formData, "delimiter", delimiter);

  return apiRequest<StatementCsvInspectionDto>(
    "/api/imports/statements/inspect",
    { method: "POST", body: formData },
    { timeoutMs: 45_000 }
  );
}

export function previewStatementCsv(
  asset: StatementDocumentAsset,
  mapping: StatementImportMappingRequest
): Promise<StatementImportPreviewDto> {
  const formData = new FormData();
  appendDocument(formData, asset);
  Object.entries(mapping).forEach(([name, value]) => appendOptionalField(formData, name, value));

  return apiRequest<StatementImportPreviewDto>(
    "/api/imports/statements/preview",
    { method: "POST", body: formData },
    { timeoutMs: 60_000 }
  );
}

export function buildStatementImportRowsPath(
  batchId: string,
  request: StatementImportRowsRequest = {}
): string {
  const searchParams = new URLSearchParams();
  appendOptionalFieldToSearch(searchParams, "cursor", request.cursor);
  appendOptionalFieldToSearch(searchParams, "pageSize", request.pageSize);
  appendOptionalFieldToSearch(searchParams, "validationStatus", request.validationStatus);
  appendOptionalFieldToSearch(
    searchParams,
    "duplicateClassification",
    request.duplicateClassification
  );
  appendOptionalFieldToSearch(searchParams, "reviewDisposition", request.reviewDisposition);
  const query = searchParams.toString();
  return `/api/imports/statements/${encodeURIComponent(batchId)}/rows${query ? `?${query}` : ""}`;
}

function appendOptionalFieldToSearch(
  searchParams: URLSearchParams,
  name: string,
  value: string | number | null | undefined
) {
  if (value !== null && value !== undefined && value !== "") {
    searchParams.append(name, String(value));
  }
}

export function getStatementImportBatch(batchId: string): Promise<StatementImportBatchDto> {
  return apiRequest<StatementImportBatchDto>(
    `/api/imports/statements/${encodeURIComponent(batchId)}`
  );
}

export function getStatementImportRows(
  batchId: string,
  request: StatementImportRowsRequest = {}
) {
  return apiRequest<StatementImportPreviewDto["rows"]>(
    buildStatementImportRowsPath(batchId, request)
  );
}

export function reviewStatementImportRows(
  batchId: string,
  request: ReviewStatementImportRowsRequest
): Promise<StatementImportReviewMutationDto> {
  return apiRequest<StatementImportReviewMutationDto>(
    `/api/imports/statements/${encodeURIComponent(batchId)}/review`,
    { method: "PATCH", body: JSON.stringify(request) }
  );
}

function mutateStatementImportLifecycle(
  batchId: string,
  action: "commit" | "discard" | "undo",
  expectedRevision: number
): Promise<StatementImportLifecycleMutationDto> {
  return apiRequest<StatementImportLifecycleMutationDto>(
    `/api/imports/statements/${encodeURIComponent(batchId)}/${action}`,
    {
      method: "POST",
      body: JSON.stringify({ expectedRevision })
    },
    { timeoutMs: 45_000 }
  );
}

export function commitStatementImport(batchId: string, expectedRevision: number) {
  return mutateStatementImportLifecycle(batchId, "commit", expectedRevision);
}

export function discardStatementImport(batchId: string, expectedRevision: number) {
  return mutateStatementImportLifecycle(batchId, "discard", expectedRevision);
}

export function undoStatementImport(batchId: string, expectedRevision: number) {
  return mutateStatementImportLifecycle(batchId, "undo", expectedRevision);
}
