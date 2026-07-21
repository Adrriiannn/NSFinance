// The enrichment pipeline keeps categorizing after the global sync request
// resolves, so the sync mutation's invalidation captures pre-backfill rows and
// the dial's interval invalidation stops the moment progress reports idle.
// Completion must trigger one final transactions invalidation, or rows
// categorized between the last interval tick and completion stay stale in
// every cached transactions query — the Activity search haystack included —
// until some unrelated refetch happens to run.
export function shouldRunEnrichmentCompletionInvalidation(
  previousHasActiveWork: boolean,
  currentHasActiveWork: boolean
): boolean {
  return previousHasActiveWork && !currentHasActiveWork;
}

// Every query root the interval loop refreshes while enrichment runs must
// also converge at completion, or that root inherits the same last-tick
// staleness the transactions fix closed: account transaction lists and the
// dashboard's recent strip read categorization results too.
export const enrichmentCompletionInvalidationRoots: readonly (readonly string[])[] = [
  ["transactions"],
  ["accounts"],
  ["dashboard"]
];
