import * as SecureStore from "expo-secure-store";
import { getAIChatThread, getAIChatThreads } from "../ai/aiChatApi";
import { buildAccountStorageKey } from "../../lib/storage/accountScope";
import { readSecureJson, writeSecureJson } from "../../lib/storage/secureAccountJsonStore";
import type { AIChatMessage, CompanionStructuredResults } from "../../types/api";

const CHAT_PRESENTATION_NAMESPACE = "nsfinance.companion.presentation.account.v1";
const COMPANION_TOOLTIP_SEEN_KEY = "nsfinance.planner.companion.tooltip_seen";
const DEFAULT_CHAT_COLOR: CompanionChatColor = "orange";

export type CompanionMessage = {
  id: string;
  role: "user" | "assistant";
  text: string;
  createdUtc: string;
  structuredResults?: CompanionStructuredResults | null;
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
  messagesLoaded: boolean;
  conversationThreadId: string | null;
  activeResultSetId: string | null;
  selectedEntityId: string | null;
  pendingClarificationSlot: string | null;
  pendingClarificationPromptIntent: string | null;
  color: CompanionChatColor;
  isPinned: boolean;
  pinnedUtc: string | null;
};

type CompanionChatPresentation = {
  title: string;
  color: CompanionChatColor;
  isPinned: boolean;
  pinnedUtc: string | null;
  activeResultSetId: string | null;
  selectedEntityId: string | null;
  pendingClarificationSlot: string | null;
  pendingClarificationPromptIntent: string | null;
};

type CompanionPresentationStore = Record<string, CompanionChatPresentation>;

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
    messages: Array.isArray(raw.messages)
      ? raw.messages.map(normalizeMessage)
      : [],
    messagesLoaded: raw.messagesLoaded !== false,
    conversationThreadId:
      typeof raw.conversationThreadId === "string" && raw.conversationThreadId.trim()
        ? raw.conversationThreadId
        : null,
    activeResultSetId:
      typeof raw.activeResultSetId === "string" && raw.activeResultSetId.trim()
        ? raw.activeResultSetId.trim()
        : null,
    selectedEntityId:
      typeof raw.selectedEntityId === "string" && raw.selectedEntityId.trim()
        ? raw.selectedEntityId.trim()
        : null,
    pendingClarificationSlot:
      typeof raw.pendingClarificationSlot === "string" && raw.pendingClarificationSlot.trim()
        ? raw.pendingClarificationSlot.trim()
        : null,
    pendingClarificationPromptIntent:
      typeof raw.pendingClarificationPromptIntent === "string" && raw.pendingClarificationPromptIntent.trim()
        ? raw.pendingClarificationPromptIntent.trim()
        : null,
    color,
    isPinned: Boolean(raw.isPinned),
    pinnedUtc: typeof raw.pinnedUtc === "string" && raw.pinnedUtc ? raw.pinnedUtc : null
  };
}

function normalizeMessage(raw: Partial<CompanionMessage>): CompanionMessage {
  return {
    id: typeof raw.id === "string" && raw.id ? raw.id : `message-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    role: raw.role === "assistant" ? "assistant" : "user",
    text: typeof raw.text === "string" ? raw.text : "",
    createdUtc: typeof raw.createdUtc === "string" && raw.createdUtc ? raw.createdUtc : new Date().toISOString(),
    structuredResults: normalizeStructuredResults(raw.structuredResults)
  };
}

function normalizeStructuredResults(value: CompanionStructuredResults | null | undefined): CompanionStructuredResults | null {
  if (!value || value.type !== "places" || !Array.isArray(value.items)) {
    return null;
  }

  const items = value.items.filter((item) =>
    typeof item?.id === "string" &&
    item.id.trim().length > 0 &&
    typeof item?.name === "string" &&
    item.name.trim().length > 0
  );
  return items.length > 0
    ? { type: "places", items }
    : null;
}

export async function getCompanionChats(userId: string): Promise<CompanionChat[]> {
  const storageKey = buildAccountStorageKey(CHAT_PRESENTATION_NAMESPACE, userId);
  const [threads, storedPresentation] = await Promise.all([
    getAIChatThreads(30),
    readSecureJson<CompanionPresentationStore>(storageKey).catch(() => null)
  ]);
  const presentation = storedPresentation ?? {};

  return threads
    .filter((thread) => thread.status.trim().toLowerCase() !== "archived")
    .map((thread) => {
      const local = presentation[thread.threadId];
      return normalizeChat({
        id: thread.threadId,
        title: local?.title || thread.title || "Cashflow conversation",
        createdUtc: thread.startedUtc,
        updatedUtc: thread.lastMessageUtc,
        messages: [],
        messagesLoaded: false,
        conversationThreadId: thread.threadId,
        activeResultSetId: local?.activeResultSetId ?? null,
        selectedEntityId: local?.selectedEntityId ?? null,
        pendingClarificationSlot: local?.pendingClarificationSlot ?? null,
        pendingClarificationPromptIntent: local?.pendingClarificationPromptIntent ?? null,
        color: local?.color ?? DEFAULT_CHAT_COLOR,
        isPinned: local?.isPinned ?? false,
        pinnedUtc: local?.pinnedUtc ?? null
      });
    });
}

export async function setCompanionChats(
  userId: string,
  chats: CompanionChat[]
): Promise<void> {
  const storageKey = buildAccountStorageKey(CHAT_PRESENTATION_NAMESPACE, userId);
  const presentation = chats.reduce<CompanionPresentationStore>((current, chat) => {
    if (!chat.conversationThreadId) {
      return current;
    }

    current[chat.conversationThreadId] = {
      title: chat.title,
      color: chat.color,
      isPinned: chat.isPinned,
      pinnedUtc: chat.pinnedUtc,
      activeResultSetId: chat.activeResultSetId,
      selectedEntityId: chat.selectedEntityId,
      pendingClarificationSlot: chat.pendingClarificationSlot,
      pendingClarificationPromptIntent: chat.pendingClarificationPromptIntent
    };
    return current;
  }, {});
  await writeSecureJson(storageKey, presentation);
}

export async function loadCompanionChatMessages(chat: CompanionChat): Promise<CompanionChat> {
  if (!chat.conversationThreadId) {
    return { ...chat, messagesLoaded: true };
  }

  const detail = await getAIChatThread(chat.conversationThreadId, 120);
  const messages = detail.messages
    .slice()
    .sort((left, right) => left.messageOrder - right.messageOrder)
    .map(mapServerMessage)
    .filter((message): message is CompanionMessage => message !== null);

  return {
    ...chat,
    createdUtc: detail.thread.startedUtc,
    updatedUtc: detail.thread.lastMessageUtc,
    messages,
    messagesLoaded: true
  };
}

function mapServerMessage(message: AIChatMessage): CompanionMessage | null {
  const role = message.role.trim().toLowerCase();
  if (role !== "user" && role !== "assistant") {
    return null;
  }

  return {
    id: message.messageId,
    role,
    text: message.content,
    createdUtc: message.createdUtc,
    structuredResults: null
  };
}

export async function deleteCompanionChat(
  userId: string,
  chatId: string,
  currentChats: CompanionChat[]
): Promise<CompanionChat[]> {
  const nextChats = currentChats.filter((chat) => chat.id !== chatId);
  await setCompanionChats(userId, nextChats);
  return nextChats;
}

export async function hasSeenCompanionTooltip(): Promise<boolean> {
  const raw = await SecureStore.getItemAsync(COMPANION_TOOLTIP_SEEN_KEY);
  return raw === "true";
}

export async function markCompanionTooltipSeen(): Promise<void> {
  await SecureStore.setItemAsync(COMPANION_TOOLTIP_SEEN_KEY, "true");
}
