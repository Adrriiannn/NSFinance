import * as SecureStore from "expo-secure-store";
import type { MfaTrustedDeviceCredentialResponse } from "../../types/api";
import {
  isMfaTrustedDeviceCredentialUsable,
  parseMfaTrustedDeviceCredential,
  type StoredMfaTrustedDeviceCredential
} from "./mfaTrustedDevicePolicy";

const MFA_TRUSTED_DEVICE_KEY = "nsfinance.auth.mfa.trusted_device";

export async function readMfaTrustedDeviceCredential({
  deviceFingerprint,
  expectedUserId
}: {
  deviceFingerprint: string | null | undefined;
  expectedUserId?: string;
}): Promise<StoredMfaTrustedDeviceCredential | null> {
  try {
    const raw = await SecureStore.getItemAsync(MFA_TRUSTED_DEVICE_KEY);
    const credential = parseMfaTrustedDeviceCredential(raw);
    if (!credential) {
      if (raw) {
        await SecureStore.deleteItemAsync(MFA_TRUSTED_DEVICE_KEY);
      }
      return null;
    }

    if (expectedUserId && credential.userId !== expectedUserId) {
      return null;
    }

    if (!isMfaTrustedDeviceCredentialUsable({
      credential,
      deviceFingerprint,
      expectedUserId
    })) {
      await SecureStore.deleteItemAsync(MFA_TRUSTED_DEVICE_KEY);
      return null;
    }

    return credential;
  } catch {
    return null;
  }
}

export async function writeMfaTrustedDeviceCredential({
  userId,
  deviceFingerprint,
  credential
}: {
  userId: string;
  deviceFingerprint: string;
  credential: MfaTrustedDeviceCredentialResponse;
}) {
  const stored: StoredMfaTrustedDeviceCredential = {
    userId,
    deviceFingerprint,
    token: credential.token,
    expiresUtc: credential.expiresUtc
  };
  await SecureStore.setItemAsync(MFA_TRUSTED_DEVICE_KEY, JSON.stringify(stored));
}

export async function clearMfaTrustedDeviceCredential() {
  await SecureStore.deleteItemAsync(MFA_TRUSTED_DEVICE_KEY);
}
