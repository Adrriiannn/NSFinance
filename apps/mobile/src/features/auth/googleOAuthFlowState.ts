import { useSyncExternalStore } from "react";
import { resetGoogleOAuthCompletionState } from "./googleOAuthCompletionState";

type GoogleOAuthFlowResetReason =
  | "logout"
  | "auth_screen_mount"
  | "auth_screen_unmount"
  | "manual_retry";

let oauthRequestEpoch = 0;
const listeners = new Set<() => void>();

function emitChange() {
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot() {
  return oauthRequestEpoch;
}

export function resetGoogleOAuthFlowState(reason: GoogleOAuthFlowResetReason) {
  void reason;
  oauthRequestEpoch += 1;
  resetGoogleOAuthCompletionState();
  emitChange();
}

export function useGoogleOAuthRequestEpoch() {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}
