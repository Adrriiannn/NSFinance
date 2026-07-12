import type { BiometricPreference } from "./biometricSecurity";

export type BiometricPreferenceStore = {
  version: 1;
  preferences: Record<string, BiometricPreference>;
};

const EMPTY_STORE: BiometricPreferenceStore = {
  version: 1,
  preferences: {}
};

function isBiometricPreference(value: unknown): value is BiometricPreference {
  if (!value || typeof value !== "object") {
    return false;
  }

  const candidate = value as Partial<BiometricPreference>;
  return typeof candidate.userId === "string"
    && candidate.userId.length > 0
    && (candidate.decision === "enabled" || candidate.decision === "declined")
    && (candidate.fallbackReviewDismissed === undefined
      || typeof candidate.fallbackReviewDismissed === "boolean");
}

export function parseBiometricPreferenceStore(raw: string | null): BiometricPreferenceStore {
  if (!raw) {
    return EMPTY_STORE;
  }

  try {
    const parsed = JSON.parse(raw) as unknown;
    if (isBiometricPreference(parsed)) {
      return {
        version: 1,
        preferences: { [parsed.userId]: parsed }
      };
    }

    if (!parsed || typeof parsed !== "object") {
      return EMPTY_STORE;
    }

    const candidate = parsed as Partial<BiometricPreferenceStore>;
    if (candidate.version !== 1 || !candidate.preferences) {
      return EMPTY_STORE;
    }

    const preferences = Object.fromEntries(
      Object.entries(candidate.preferences)
        .filter((entry): entry is [string, BiometricPreference] => isBiometricPreference(entry[1]))
        .map(([, preference]) => [preference.userId, preference])
    );
    return { version: 1, preferences };
  } catch {
    return EMPTY_STORE;
  }
}

export function setBiometricPreference(
  store: BiometricPreferenceStore,
  preference: BiometricPreference
): BiometricPreferenceStore {
  return {
    version: 1,
    preferences: {
      ...store.preferences,
      [preference.userId]: preference
    }
  };
}
