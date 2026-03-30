export type ActivityFocusRoute = {
  pathname: "/(tabs)/activity";
  params: {
    focusTransactionId: string;
    focusNonce: string;
  };
};

function normalizeTransactionId(transactionId: string) {
  return transactionId.trim();
}

export function buildActivityFocusRoute(
  transactionId: string,
  focusNonce: string = Date.now().toString()
): ActivityFocusRoute | null {
  const normalizedTransactionId = normalizeTransactionId(transactionId);
  if (!normalizedTransactionId) {
    return null;
  }

  return {
    pathname: "/(tabs)/activity",
    params: {
      focusTransactionId: normalizedTransactionId,
      focusNonce
    }
  };
}

export function logActivityFocusEvent(event: string, metadata?: Record<string, unknown>) {
  console.info("[Activity Focus]", {
    event,
    timestampUtc: new Date().toISOString(),
    ...metadata
  });
}
