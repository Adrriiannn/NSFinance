import * as SecureStore from "expo-secure-store";

export type EnrichmentDialState = {
  left: number;
  top: number;
  dismissedCompletedSignature: string | null;
};

const ENRICHMENT_DIAL_STORAGE_PREFIX = "nsfinance.enrichmentDial";

function buildEnrichmentDialStorageKey(userId?: string | null) {
  return `${ENRICHMENT_DIAL_STORAGE_PREFIX}.${userId ?? "guest"}`;
}

export async function getEnrichmentDialState(
  userId?: string | null
): Promise<EnrichmentDialState | null> {
  try {
    const raw = await SecureStore.getItemAsync(buildEnrichmentDialStorageKey(userId));
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<EnrichmentDialState>;
    if (
      typeof parsed.left === "number"
      && Number.isFinite(parsed.left)
      && typeof parsed.top === "number"
      && Number.isFinite(parsed.top)
    ) {
      return {
        left: parsed.left,
        top: parsed.top,
        dismissedCompletedSignature:
          typeof parsed.dismissedCompletedSignature === "string"
            ? parsed.dismissedCompletedSignature
            : null
      };
    }

    return null;
  } catch {
    return null;
  }
}

export async function setEnrichmentDialState(
  state: EnrichmentDialState,
  userId?: string | null
): Promise<void> {
  try {
    await SecureStore.setItemAsync(buildEnrichmentDialStorageKey(userId), JSON.stringify(state));
  } catch {
    // Best-effort persistence only.
  }
}
