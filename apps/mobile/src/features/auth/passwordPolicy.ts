import zxcvbn from "zxcvbn";

export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 64;

export type PasswordStrengthTier = "very_weak" | "weak" | "fair" | "strong" | "very_strong";

export type PasswordStrengthResult = {
  score: number;
  tier: PasswordStrengthTier;
  label: "Very weak" | "Weak" | "Fair" | "Strong" | "Very strong";
};

export type PasswordBreachStatus = "idle" | "checking" | "safe" | "compromised" | "unavailable";

export function sanitizePasswordInput(value: string): string {
  return value.replace(/\s+/g, "");
}

export function hasNumberOrSymbol(password: string): boolean {
  return /\d/.test(password) || /[^A-Za-z0-9]/.test(password);
}

export function isLengthWithinPolicy(password: string): boolean {
  return password.length >= PASSWORD_MIN_LENGTH && password.length <= PASSWORD_MAX_LENGTH;
}

export function enforcePasswordMaxLength(password: string): string {
  if (password.length <= PASSWORD_MAX_LENGTH) {
    return password;
  }

  return password.slice(0, PASSWORD_MAX_LENGTH);
}

export function evaluatePasswordStrength(password: string): PasswordStrengthResult | null {
  if (!password) {
    return null;
  }

  const { score } = zxcvbn(password);

  if (score <= 0) {
    return { score, tier: "very_weak", label: "Very weak" };
  }
  if (score === 1) {
    return { score, tier: "weak", label: "Weak" };
  }
  if (score === 2) {
    return { score, tier: "fair", label: "Fair" };
  }
  if (score === 3) {
    return { score, tier: "strong", label: "Strong" };
  }

  return { score, tier: "very_strong", label: "Very strong" };
}
