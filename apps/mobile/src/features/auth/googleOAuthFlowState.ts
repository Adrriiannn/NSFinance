import { useSyncExternalStore } from "react";
import { resetGoogleOAuthDebugState } from "./googleOAuthDebug";

type GoogleOAuthFlowResetReason =
  | "logout"
  | "auth_screen_mount"
  | "auth_screen_unmount"
  | "code_exchange_failed"
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

function logFlowReset(reason: GoogleOAuthFlowResetReason) {
  if (!__DEV__) {
    return;
  }

  console.info("[GoogleAuth] flow_reset", {
    reason,
    oauthRequestEpoch
  });
}

export function resetGoogleOAuthFlowState(reason: GoogleOAuthFlowResetReason) {
  oauthRequestEpoch += 1;
  resetGoogleOAuthDebugState();
  logFlowReset(reason);
  emitChange();
}

export function useGoogleOAuthRequestEpoch() {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}

