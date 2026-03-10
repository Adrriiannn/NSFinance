export type FlashMessagePayload = {
  id: number;
  message: string;
  durationMs?: number;
};

type FlashMessageListener = (payload: FlashMessagePayload) => void;

const listeners = new Set<FlashMessageListener>();
let pendingPayload: FlashMessagePayload | null = null;

export function showFlashMessage(message: string, durationMs = 1800) {
  const payload: FlashMessagePayload = {
    id: Date.now() + Math.floor(Math.random() * 1000),
    message,
    durationMs
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
