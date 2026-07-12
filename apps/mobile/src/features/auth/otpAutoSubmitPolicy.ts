export const OTP_CODE_LENGTH = 6;

export function normalizeOtpCode(value: string): string {
  return value.replace(/\D/g, "").slice(0, OTP_CODE_LENGTH);
}

export function buildOtpAttemptKey(challengeId: string, value: string): string | null {
  const normalizedChallengeId = challengeId.trim();
  const normalizedCode = normalizeOtpCode(value);
  if (!normalizedChallengeId || normalizedCode.length !== OTP_CODE_LENGTH) {
    return null;
  }

  return `${normalizedChallengeId}:${normalizedCode}`;
}

export function shouldAutoSubmitOtp({
  challengeId,
  code,
  isPending,
  lastAttemptKey
}: {
  challengeId: string;
  code: string;
  isPending: boolean;
  lastAttemptKey: string | null;
}): boolean {
  if (isPending) {
    return false;
  }

  const attemptKey = buildOtpAttemptKey(challengeId, code);
  return attemptKey !== null && attemptKey !== lastAttemptKey;
}
