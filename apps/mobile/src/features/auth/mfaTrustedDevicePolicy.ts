export type StoredMfaTrustedDeviceCredential = {
  userId: string;
  deviceFingerprint: string;
  token: string;
  expiresUtc: string;
};

export function parseMfaTrustedDeviceCredential(
  raw: string | null
): StoredMfaTrustedDeviceCredential | null {
  if (!raw) {
    return null;
  }

  try {
    const candidate = JSON.parse(raw) as Partial<StoredMfaTrustedDeviceCredential>;
    return typeof candidate.userId === "string"
      && candidate.userId.length > 0
      && typeof candidate.deviceFingerprint === "string"
      && candidate.deviceFingerprint.length > 0
      && typeof candidate.token === "string"
      && candidate.token.length >= 32
      && typeof candidate.expiresUtc === "string"
      && Number.isFinite(Date.parse(candidate.expiresUtc))
      ? candidate as StoredMfaTrustedDeviceCredential
      : null;
  } catch {
    return null;
  }
}

export function isMfaTrustedDeviceCredentialUsable({
  credential,
  deviceFingerprint,
  expectedUserId,
  nowMs = Date.now()
}: {
  credential: StoredMfaTrustedDeviceCredential | null;
  deviceFingerprint: string | null | undefined;
  expectedUserId?: string;
  nowMs?: number;
}): boolean {
  return Boolean(
    credential
    && deviceFingerprint
    && credential.deviceFingerprint === deviceFingerprint
    && (!expectedUserId || credential.userId === expectedUserId)
    && Date.parse(credential.expiresUtc) > nowMs
  );
}
