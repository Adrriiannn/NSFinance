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

  if (result.outcome === "skipped_not_due") {
    return {
      tone: "info" as const,
      message: "Bank data is already up to date for now."
    };
  }

  if (result.failedConnectionCount > 0) {
    return {
      tone: "error" as const,
      message: "Sync finished with partial issues. Some bank connections need attention."
    };
  }

  if (result.changedConnectionCount > 0) {
    return {
      tone: "success" as const,
      message: "The accounts have been refreshed."
    };
  }

  return {
    tone: "info" as const,
    message: "Sync completed. No new booked updates were available."
  };
}
