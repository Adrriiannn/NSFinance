import { apiRequest } from "../../lib/api/client";
import type {
  CreateExpenseTrackerEntryRequest,
  ExpenseTaxonomyResponseDto,
  ExpenseTrackerEntryDto,
  UpdateExpenseTrackerEntryRequest
} from "../../types/api";

export function getExpenseTrackerTaxonomy() {
  return apiRequest<ExpenseTaxonomyResponseDto>("/api/expense-tracker/taxonomy");
}

export function getExpenseTrackerEntries() {
  return apiRequest<ExpenseTrackerEntryDto[]>("/api/expense-tracker/entries");
}

export function getExpenseTrackerEntryById(entryId: string) {
  return apiRequest<ExpenseTrackerEntryDto>(`/api/expense-tracker/entries/${entryId}`);
}

export function createExpenseTrackerEntry(payload: CreateExpenseTrackerEntryRequest) {
  return apiRequest<ExpenseTrackerEntryDto>("/api/expense-tracker/entries", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function updateExpenseTrackerEntry(
  entryId: string,
  payload: UpdateExpenseTrackerEntryRequest
) {
  return apiRequest<ExpenseTrackerEntryDto>(`/api/expense-tracker/entries/${entryId}`, {
    method: "PUT",
    body: JSON.stringify(payload)
  });
}

export function deleteExpenseTrackerEntry(entryId: string) {
  return apiRequest<void>(`/api/expense-tracker/entries/${entryId}`, {
    method: "DELETE"
  });
}
