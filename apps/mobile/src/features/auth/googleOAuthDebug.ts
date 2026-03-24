import { useSyncExternalStore } from "react";

export type GoogleOAuthDebugState = {
  currentStep: string;
  lines: string[];
  hasParamsIdToken: boolean;
  hasAuthenticationIdToken: boolean;
  hasCode: boolean;
  hasError: boolean;
  idTokenPresent: boolean;
  idTokenLength: number;
  idTokenPrefix: string;
  backendCalled: boolean;
  backendOutcome: "none" | "success" | "failure";
  backendMessage: string;
  updatedAt: number;
};

const INITIAL_STATE: GoogleOAuthDebugState = {
  currentStep: "idle",
  lines: [],
  hasParamsIdToken: false,
  hasAuthenticationIdToken: false,
  hasCode: false,
  hasError: false,
  idTokenPresent: false,
  idTokenLength: 0,
  idTokenPrefix: "",
  backendCalled: false,
  backendOutcome: "none",
  backendMessage: "",
  updatedAt: Date.now()
};

let state: GoogleOAuthDebugState = INITIAL_STATE;
const listeners = new Set<() => void>();

function emitChange() {
  listeners.forEach((listener) => listener());
}

function withTimestamp(message: string) {
  const now = new Date();
  const hh = String(now.getHours()).padStart(2, "0");
  const mm = String(now.getMinutes()).padStart(2, "0");
  const ss = String(now.getSeconds()).padStart(2, "0");
  return `${hh}:${mm}:${ss} ${message}`;
}

export function resetGoogleOAuthDebugState() {
  state = { ...INITIAL_STATE, updatedAt: Date.now() };
  emitChange();
}

export function updateGoogleOAuthDebugState(patch: Partial<GoogleOAuthDebugState>) {
  state = {
    ...state,
    ...patch,
    updatedAt: Date.now()
  };
  emitChange();
}

export function pushGoogleOAuthDebugStep(step: string, detail?: string) {
  const message = detail ? `${step} - ${detail}` : step;
  const nextLine = withTimestamp(message);
  const nextLines = [...state.lines, nextLine].slice(-24);

  state = {
    ...state,
    currentStep: step,
    lines: nextLines,
    updatedAt: Date.now()
  };
  emitChange();
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

export function useGoogleOAuthDebugState() {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}
