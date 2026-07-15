export type AccountDetailsSectionState = "loading" | "error" | "stale" | "ready";

export type AccountDetailsQuerySnapshot = {
  hasData: boolean;
  isLoading: boolean;
  hasError: boolean;
  hasTerminalError?: boolean;
};

export function resolveAccountDetailsSectionState(
  snapshots: readonly AccountDetailsQuerySnapshot[]
): AccountDetailsSectionState {
  if (snapshots.some((snapshot) => snapshot.hasTerminalError)) {
    return "error";
  }

  const hasAllData = snapshots.length > 0 && snapshots.every((snapshot) => snapshot.hasData);
  if (hasAllData) {
    return snapshots.some((snapshot) => snapshot.hasError) ? "stale" : "ready";
  }

  const missingData = snapshots.filter((snapshot) => !snapshot.hasData);
  if (missingData.some((snapshot) => snapshot.hasError)) {
    return "error";
  }

  if (missingData.some((snapshot) => snapshot.isLoading)) {
    return "loading";
  }

  return "loading";
}
