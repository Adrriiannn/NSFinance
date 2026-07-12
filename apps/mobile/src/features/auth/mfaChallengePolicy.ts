export function getMfaChallengeRemainingMs(expiresUtc: string, nowMs = Date.now()) {
  const expiresAtMs = Date.parse(expiresUtc);
  if (Number.isNaN(expiresAtMs)) {
    return 0;
  }

  return Math.max(0, expiresAtMs - nowMs);
}

export function isMfaChallengeExpired(expiresUtc: string, nowMs = Date.now()) {
  return getMfaChallengeRemainingMs(expiresUtc, nowMs) === 0;
}
