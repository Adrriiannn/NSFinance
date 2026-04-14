import { apiRequest } from "../../lib/api/client";
import type { SendAIChatMessageRequest, SendAIChatMessageResponse } from "../../types/api";

export async function sendAIChatMessage(
  payload: SendAIChatMessageRequest
): Promise<SendAIChatMessageResponse> {
  return apiRequest<SendAIChatMessageResponse>("/api/ai/chat/send", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}
