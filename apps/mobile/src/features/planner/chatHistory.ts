import * as SecureStore from "expo-secure-store";
import {
  readJsonFileStorage,
  writeJsonFileStorage
} from "../../lib/storage/jsonFileStore";

const CHAT_HISTORY_KEY = "nsfinance.planner.companion.chat_history";
const COMPANION_TOOLTIP_SEEN_KEY = "nsfinance.planner.companion.tooltip_seen";
const CHAT_SUMMARY_PREFIX = "nsfinance.companion.chat.summary.";
const CHAT_MEMORY_PREFIX = "nsfinance.companion.chat.memory.";
const CHAT_RETRIEVAL_PREFIX = "nsfinance.companion.chat.retrieval.";
const CHAT_INDEX_PREFIX = "nsfinance.companion.chat.index.";
const DEFAULT_CHAT_COLOR: CompanionChatColor = "blue";
let companionChatsCache: CompanionChat[] | null = null;
let companionChatsLoadPromise: Promise<CompanionChat[]> | null = null;

export type CompanionMessage = {
  id: string;
  role: "user" | "assistant";
  text: string;
  createdUtc: string;
};

export type CompanionChatColor =
  | "blue"
  | "yellow"
  | "green"
  | "pink"
  | "red"
  | "white"
  | "orange"
  | "purple"
  | "brown";

export type CompanionChat = {
  id: string;
  title: string;
  createdUtc: string;
  updatedUtc: string;
  messages: CompanionMessage[];
  color: CompanionChatColor;
  isPinned: boolean;
  pinnedUtc: string | null;
};

function isValidChatColor(value: string): value is CompanionChatColor {
  return (
    value === "blue" ||
    value === "yellow" ||
    value === "green" ||
    value === "pink" ||
    value === "red" ||
    value === "white" ||
    value === "orange" ||
    value === "purple" ||
    value === "brown"
  );
}

function normalizeChat(raw: Partial<CompanionChat>): CompanionChat {
  const createdUtc = typeof raw.createdUtc === "string" && raw.createdUtc ? raw.createdUtc : new Date().toISOString();
  const updatedUtc = typeof raw.updatedUtc === "string" && raw.updatedUtc ? raw.updatedUtc : createdUtc;
  const rawColor = typeof raw.color === "string" ? raw.color : DEFAULT_CHAT_COLOR;
  const color = isValidChatColor(rawColor) ? rawColor : DEFAULT_CHAT_COLOR;

  return {
    id: raw.id ?? `chat-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    title: typeof raw.title === "string" && raw.title.trim() ? raw.title.trim() : "New conversation",
    createdUtc,
    updatedUtc,
    messages: Array.isArray(raw.messages) ? raw.messages : [],
    color,
    isPinned: Boolean(raw.isPinned),
    pinnedUtc: typeof raw.pinnedUtc === "string" && raw.pinnedUtc ? raw.pinnedUtc : null
  };
}

function cloneChats(chats: CompanionChat[]): CompanionChat[] {
  return chats.map((chat) => ({
    ...chat,
    messages: [...chat.messages]
  }));
}

export async function getCompanionChats(): Promise<CompanionChat[]> {
  if (companionChatsCache) {
    return cloneChats(companionChatsCache);
  }

  if (companionChatsLoadPromise) {
    return cloneChats(await companionChatsLoadPromise);
  }

  companionChatsLoadPromise = (async () => {
    try {
      const stored = await readJsonFileStorage<Partial<CompanionChat>[]>(CHAT_HISTORY_KEY);
      if (stored) {
        const normalizedStored = stored.map(normalizeChat);
        companionChatsCache = normalizedStored;
        return cloneChats(normalizedStored);
      }

      const legacyRaw = await SecureStore.getItemAsync(CHAT_HISTORY_KEY);
      if (!legacyRaw) {
        companionChatsCache = [];
        return [];
      }

      const parsed = JSON.parse(legacyRaw) as Partial<CompanionChat>[];
      if (!Array.isArray(parsed)) {
        companionChatsCache = [];
        return [];
      }

      const normalized = parsed.map(normalizeChat);
      await writeJsonFileStorage(CHAT_HISTORY_KEY, normalized);
      await SecureStore.deleteItemAsync(CHAT_HISTORY_KEY);
      companionChatsCache = normalized;
      return cloneChats(normalized);
    } catch {
      companionChatsCache = [];
      return [];
    }
  })();

  try {
    return cloneChats(await companionChatsLoadPromise);
  } finally {
    companionChatsLoadPromise = null;
  }
}

export async function setCompanionChats(chats: CompanionChat[]): Promise<void> {
  const normalized = chats.map(normalizeChat);
  companionChatsCache = normalized;
  await writeJsonFileStorage(CHAT_HISTORY_KEY, normalized);
}

export async function deleteCompanionChatArtifacts(chatId: string): Promise<void> {
  const keys = [
    `${CHAT_SUMMARY_PREFIX}${chatId}`,
    `${CHAT_MEMORY_PREFIX}${chatId}`,
    `${CHAT_RETRIEVAL_PREFIX}${chatId}`,
    `${CHAT_INDEX_PREFIX}${chatId}`
  ];

  await Promise.all(keys.map((key) => SecureStore.deleteItemAsync(key)));
}

export async function deleteCompanionChat(chatId: string): Promise<CompanionChat[]> {
  const chats = await getCompanionChats();
  const nextChats = chats.filter((chat) => chat.id !== chatId);
  companionChatsCache = nextChats;
  await setCompanionChats(nextChats);
  await deleteCompanionChatArtifacts(chatId);
  return nextChats;
}

export async function hasSeenCompanionTooltip(): Promise<boolean> {
  const raw = await SecureStore.getItemAsync(COMPANION_TOOLTIP_SEEN_KEY);
  return raw === "true";
}

export async function markCompanionTooltipSeen(): Promise<void> {
  await SecureStore.setItemAsync(COMPANION_TOOLTIP_SEEN_KEY, "true");
}
