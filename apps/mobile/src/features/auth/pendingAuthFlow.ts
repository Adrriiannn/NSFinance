import type { CodeDeliveryResponse, MfaLoginChallengeResponse } from "../../types/api";

export type PendingEmailVerification = CodeDeliveryResponse & {
  email?: string;
  rememberMe: boolean;
};

export type PendingMfaLogin = MfaLoginChallengeResponse & {
  rememberMe: boolean;
};

let pendingEmailVerification: PendingEmailVerification | null = null;
let pendingMfaLogin: PendingMfaLogin | null = null;

export function stageEmailVerification(value: PendingEmailVerification) {
  pendingEmailVerification = value;
  pendingMfaLogin = null;
}

export function getPendingEmailVerification(): PendingEmailVerification | null {
  return pendingEmailVerification;
}

export function clearPendingEmailVerification() {
  pendingEmailVerification = null;
}

export function stageMfaLogin(value: PendingMfaLogin) {
  pendingMfaLogin = value;
  pendingEmailVerification = null;
}

export function getPendingMfaLogin(): PendingMfaLogin | null {
  return pendingMfaLogin;
}

export function clearPendingMfaLogin() {
  pendingMfaLogin = null;
}

export function clearPendingAuthFlows() {
  pendingEmailVerification = null;
  pendingMfaLogin = null;
}
