import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  CreateExpenseTrackerEntryRequest,
  ExpenseTrackerEntryDto,
  UpdateExpenseTrackerEntryRequest
} from "../../types/api";
import {
  createExpenseTrackerEntry,
  deleteExpenseTrackerEntry,
  getExpenseTrackerEntryById,
  getExpenseTrackerEntries,
  getExpenseTrackerTaxonomy,
  updateExpenseTrackerEntry
} from "./expenseTrackerApi";

const trackerQueryOptions = {
  staleTime: 30_000,
  refetchOnMount: false,
  refetchOnWindowFocus: false,
  refetchOnReconnect: true,
  refetchInterval: false as const,
  refetchIntervalInBackground: false
};

function upsertEntry(
  entries: ExpenseTrackerEntryDto[] | undefined,
  nextEntry: ExpenseTrackerEntryDto
) {
  const current = entries ?? [];
  const existingIndex = current.findIndex((item) => item.id === nextEntry.id);
  if (existingIndex < 0) {
    return [nextEntry, ...current].sort(
      (left, right) => new Date(right.occurredAtUtc).getTime() - new Date(left.occurredAtUtc).getTime()
    );
  }

  const copy = [...current];
  copy[existingIndex] = nextEntry;
  return copy.sort(
    (left, right) => new Date(right.occurredAtUtc).getTime() - new Date(left.occurredAtUtc).getTime()
  );
}

export function useExpenseTrackerTaxonomyQuery() {
  return useQuery({
    queryKey: queryKeys.expenseTracker.taxonomy,
    queryFn: getExpenseTrackerTaxonomy,
    staleTime: 12 * 60 * 60_000,
    refetchOnMount: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: true
  });
}

export function useExpenseTrackerEntriesQuery() {
  return useQuery({
    queryKey: queryKeys.expenseTracker.entries,
    queryFn: getExpenseTrackerEntries,
    ...trackerQueryOptions
  });
}

export function useExpenseTrackerEntryDetailQuery(entryId: string) {
  return useQuery({
    queryKey: queryKeys.expenseTracker.detail(entryId),
    queryFn: () => getExpenseTrackerEntryById(entryId),
    enabled: Boolean(entryId),
    ...trackerQueryOptions
  });
}

export function useCreateExpenseTrackerEntryMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: CreateExpenseTrackerEntryRequest) => createExpenseTrackerEntry(payload),
    onSuccess: async (entry) => {
      queryClient.setQueryData<ExpenseTrackerEntryDto[]>(
        queryKeys.expenseTracker.entries,
        (current) => upsertEntry(current, entry)
      );
      queryClient.setQueryData(queryKeys.expenseTracker.detail(entry.id), entry);
      await queryClient.invalidateQueries({ queryKey: queryKeys.expenseTracker.root });
    }
  });
}

export function useUpdateExpenseTrackerEntryMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ entryId, payload }: { entryId: string; payload: UpdateExpenseTrackerEntryRequest }) =>
      updateExpenseTrackerEntry(entryId, payload),
    onSuccess: async (entry) => {
      queryClient.setQueryData<ExpenseTrackerEntryDto[]>(
        queryKeys.expenseTracker.entries,
        (current) => upsertEntry(current, entry)
      );
      queryClient.setQueryData(queryKeys.expenseTracker.detail(entry.id), entry);
      await queryClient.invalidateQueries({ queryKey: queryKeys.expenseTracker.root });
    }
  });
}

export function useDeleteExpenseTrackerEntryMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (entryId: string) => deleteExpenseTrackerEntry(entryId),
    onSuccess: async (_result, entryId) => {
      queryClient.setQueryData<ExpenseTrackerEntryDto[]>(
        queryKeys.expenseTracker.entries,
        (current) => (current ?? []).filter((item) => item.id !== entryId)
      );
      queryClient.removeQueries({ queryKey: queryKeys.expenseTracker.detail(entryId) });
      await queryClient.invalidateQueries({ queryKey: queryKeys.expenseTracker.root });
    }
  });
}
