import * as SecureStore from "expo-secure-store";

const CHAT_HISTORY_KEY = "nsfintech.planner.companion.chat_history";
const COMPANION_TOOLTIP_SEEN_KEY = "nsfintech.planner.companion.tooltip_seen";

export type CompanionMessage = {
  id: string;
  role: "user" | "assistant";
  text: string;
  createdUtc: string;
};

export type CompanionChat = {
  id: string;
  title: string;
  createdUtc: string;
  updatedUtc: string;
  messages: CompanionMessage[];
};

export async function getCompanionChats(): Promise<CompanionChat[]> {
  try {
    const raw = await SecureStore.getItemAsync(CHAT_HISTORY_KEY);
    if (!raw) {
      return [];
    }

    const parsed = JSON.parse(raw) as CompanionChat[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export async function setCompanionChats(chats: CompanionChat[]): Promise<void> {
  await SecureStore.setItemAsync(CHAT_HISTORY_KEY, JSON.stringify(chats));
}

export async function hasSeenCompanionTooltip(): Promise<boolean> {
  const raw = await SecureStore.getItemAsync(COMPANION_TOOLTIP_SEEN_KEY);
  return raw === "true";
}

export async function markCompanionTooltipSeen(): Promise<void> {
  await SecureStore.setItemAsync(COMPANION_TOOLTIP_SEEN_KEY, "true");
}
