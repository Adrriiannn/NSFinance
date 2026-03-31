import type { GlobalBankSyncResponse } from "../../types/api";

function padToTwoDigits(value: number) {
  return value.toString().padStart(2, "0");
}

export function formatSyncCooldown(remainingSeconds: number) {
  const safeSeconds = Math.max(0, Math.floor(remainingSeconds));
  const minutes = Math.floor(safeSeconds / 60);
  const seconds = safeSeconds % 60;
  return `${padToTwoDigits(minutes)}:${padToTwoDigits(seconds)}`;
}

export function getGlobalSyncFeedbackMessage(result: GlobalBankSyncResponse) {
  if (result.outcome === "failed_unexpected") {
    return {
      tone: "error" as const,
      message: "Sync failed. Please try again later."
    };
  }

  if (result.outcome === "skipped_cooldown") {
    return {
      tone: "info" as const,
      message: `Sync is on cooldown. Try again in ${formatSyncCooldown(result.cooldownRemainingSeconds)}.`
    };
  }

  if (result.outcome === "skipped_no_eligible_connections") {
    return {
      tone: "info" as const,
      message: "No eligible linked banks are available to sync right now."
    };
  }

  if (result.outcome === "skipped_provider_backoff") {
    return {
      tone: "info" as const,
      message: "Your bank asked us to slow down. Sync will retry after the provider cooldown."
    };
  }

  if (result.outcome === "skipped_not_due") {
    return {
      tone: "info" as const,
      message: "Bank data is already up to date for now."
    };
  }

  if (result.failedConnectionCount > 0) {
    const hasReauthFailure = result.connections.some(
      (connection) =>
        connection.errorCode === "bank_connection_reauth_required"
        || connection.errorCode === "refresh_token_missing"
        || connection.errorCode === "refresh_token_invalid"
        || connection.status === "reauth_required"
    );

    if (hasReauthFailure) {
      return {
        tone: "error" as const,
        message: "Some banks need reconnection before syncing."
      };
    }

    return {
      tone: "error" as const,
      message: "Sync finished with partial issues. Some bank connections need attention."
    };
  }

  if (result.changedConnectionCount > 0) {
    const onlyBalanceOrPendingChanges = result.connections.every((connection) =>
      connection.dataChanged
        ? connection.freshnessSummary === "no_newer_rows_returned"
          || connection.freshnessSummary === "pending_only_rows_returned"
          || connection.freshnessSummary === "no_rows_returned"
        : true
    );

    if (onlyBalanceOrPendingChanges) {
      return {
        tone: "success" as const,
        message: "Sync completed. Balances or pending activity updated."
      };
    }

    return {
      tone: "success" as const,
      message: "The accounts have been refreshed."
    };
  }

  if (result.noNewerRowsConnectionCount > 0) {
    return {
      tone: "info" as const,
      message: "Sync completed. No newer provider rows were available yet."
    };
  }

  return {
    tone: "info" as const,
    message: "Sync completed. No new booked updates were available."
  };
}
