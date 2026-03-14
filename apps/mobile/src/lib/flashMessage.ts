export type FlashMessageTone = "success" | "error" | "info";

export type FlashMessagePayload = {
  id: number;
  message: string;
  durationMs?: number;
  tone: FlashMessageTone;
};

type FlashMessageListener = (payload: FlashMessagePayload) => void;

export type FlashMessageOptions = {
  durationMs?: number;
  tone?: FlashMessageTone;
};

const listeners = new Set<FlashMessageListener>();
let pendingPayload: FlashMessagePayload | null = null;

export function showFlashMessage(message: string, options?: number | FlashMessageOptions) {
  const normalizedOptions =
    typeof options === "number"
      ? { durationMs: options, tone: "success" as FlashMessageTone }
      : { durationMs: options?.durationMs, tone: options?.tone ?? "success" as FlashMessageTone };

  const payload: FlashMessagePayload = {
    id: Date.now() + Math.floor(Math.random() * 1000),
    message,
    durationMs: normalizedOptions.durationMs,
    tone: normalizedOptions.tone
  };

  if (!listeners.size) {
    pendingPayload = payload;
    return;
  }

  listeners.forEach((listener) => listener(payload));
}

export function subscribeToFlashMessages(listener: FlashMessageListener) {
  listeners.add(listener);

  if (pendingPayload) {
    listener(pendingPayload);
    pendingPayload = null;
  }

  return () => {
    listeners.delete(listener);
  };
}
