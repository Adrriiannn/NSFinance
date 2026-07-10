import { useSyncExternalStore } from "react";

export type GoogleOAuthCompletionStatus = "idle" | "pending" | "success" | "failure";

type GoogleOAuthCompletionState = {
  status: GoogleOAuthCompletionStatus;
  message: string;
};

const INITIAL_STATE: GoogleOAuthCompletionState = {
  status: "idle",
  message: ""
};

let state = INITIAL_STATE;
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
  return state;
}

export function getGoogleOAuthCompletionState() {
  return state;
}

export function resetGoogleOAuthCompletionState() {
  state = INITIAL_STATE;
  emitChange();
}

export function setGoogleOAuthCompletionState(
  status: GoogleOAuthCompletionStatus,
  message = ""
) {
  state = { status, message };
  emitChange();
}

export function useGoogleOAuthCompletionState() {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}
