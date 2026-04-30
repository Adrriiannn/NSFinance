import { apiRequest } from "../../lib/api/client";
import type { SendAIChatMessageRequest, SendAIChatMessageResponse } from "../../types/api";

const CHAT_TURN_TIMEOUT_MS = 45_000;

export async function sendAIChatMessage(
  payload: SendAIChatMessageRequest
): Promise<SendAIChatMessageResponse> {
  const response = await apiRequest<SendAIChatMessageResponse>("/api/ai/chat/send", {
    method: "POST",
    body: JSON.stringify(payload)
  }, {
    // Chat turns can include persistence, retrieval, Places calls, and AI composition.
    // Keep this budget scoped to chat so normal API calls retain the tighter default.
    timeoutMs: CHAT_TURN_TIMEOUT_MS
  });

  return {
    ...response,
    message: sanitizeAssistantMessage(response.message, response.succeeded)
  };
}

function sanitizeAssistantMessage(message: string, succeeded: boolean): string {
  const trimmed = message.trim();
  if (trimmed.length === 0) {
    return message;
  }

  if (!(trimmed.startsWith("{") || trimmed.startsWith("["))) {
    return message;
  }

  try {
    const parsed = JSON.parse(trimmed) as Record<string, unknown>;
    const candidate = readReplyAlias(parsed);
    if (candidate) {
      return candidate;
    }
  } catch {
    // Keep the fallback below if the payload is object-shaped but not valid JSON.
  }

  return succeeded
    ? "I found results, but I couldn't format the reply cleanly. Please retry."
    : message;
}

function readReplyAlias(value: Record<string, unknown>): string | null {
  const aliases = ["replyText", "reply_text", "reply", "message", "content", "text"] as const;
  for (const alias of aliases) {
    const candidate = value[alias];
    if (typeof candidate === "string" && candidate.trim().length > 0) {
      return candidate.trim();
    }
  }

  return null;
}
