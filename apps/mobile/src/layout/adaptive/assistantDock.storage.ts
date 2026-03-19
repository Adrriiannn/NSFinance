import * as SecureStore from "expo-secure-store";

export type AssistantDockMode = "docked" | "expanded";
export type AssistantDockSide = "left" | "right";
export type AssistantDockState = {
  mode: AssistantDockMode;
  side: AssistantDockSide;
  verticalRatio: number;
};

const ASSISTANT_DOCK_STORAGE_PREFIX = "nsfinance.assistantDock";

function buildAssistantDockStorageKey(userId?: string | null) {
  return `${ASSISTANT_DOCK_STORAGE_PREFIX}.${userId ?? "guest"}`;
}

export async function getAssistantDockState(
  userId?: string | null
): Promise<AssistantDockState | null> {
  try {
    const raw = await SecureStore.getItemAsync(buildAssistantDockStorageKey(userId));
    if (!raw) {
      return null;
    }

    if (raw === "expanded" || raw === "docked") {
      return {
        mode: raw,
        side: "right",
        verticalRatio: 1
      };
    }

    const parsed = JSON.parse(raw) as Partial<AssistantDockState>;
    if (
      (parsed.mode === "expanded" || parsed.mode === "docked") &&
      (parsed.side === "left" || parsed.side === "right")
    ) {
      return {
        mode: parsed.mode,
        side: parsed.side,
        verticalRatio:
          typeof parsed.verticalRatio === "number" && Number.isFinite(parsed.verticalRatio)
            ? Math.max(0, Math.min(parsed.verticalRatio, 1))
            : 1
      };
    }

    return null;
  } catch {
    return null;
  }
}

export async function setAssistantDockState(
  state: AssistantDockState,
  userId?: string | null
): Promise<void> {
  try {
    await SecureStore.setItemAsync(buildAssistantDockStorageKey(userId), JSON.stringify(state));
  } catch {
    // Ignore persistence failures and keep the in-memory state responsive.
  }
}
