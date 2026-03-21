import * as SecureStore from "expo-secure-store";

const REMEMBER_EMAIL_ENABLED_KEY = "nsfinance.auth.remember_email.enabled";
const REMEMBER_EMAIL_VALUE_KEY = "nsfinance.auth.remember_email.value";

export type RememberedEmailState = {
  enabled: boolean;
  email: string;
};

export async function readRememberedEmail(): Promise<RememberedEmailState> {
  try {
    const [enabledRaw, emailRaw] = await Promise.all([
      SecureStore.getItemAsync(REMEMBER_EMAIL_ENABLED_KEY),
      SecureStore.getItemAsync(REMEMBER_EMAIL_VALUE_KEY)
    ]);

    const enabled = enabledRaw === "true";
    const email = (emailRaw ?? "").trim();

    if (!enabled || !email) {
      return { enabled: false, email: "" };
    }

    return {
      enabled: true,
      email
    };
  } catch {
    return { enabled: false, email: "" };
  }
}

export async function persistRememberedEmail(enabled: boolean, email: string) {
  try {
    const normalizedEmail = email.trim().toLowerCase();

    if (!enabled || normalizedEmail.length === 0) {
      await Promise.all([
        SecureStore.deleteItemAsync(REMEMBER_EMAIL_ENABLED_KEY),
        SecureStore.deleteItemAsync(REMEMBER_EMAIL_VALUE_KEY)
      ]);
      return;
    }

    await Promise.all([
      SecureStore.setItemAsync(REMEMBER_EMAIL_ENABLED_KEY, "true"),
      SecureStore.setItemAsync(REMEMBER_EMAIL_VALUE_KEY, normalizedEmail)
    ]);
  } catch {
    // Ignore local remember-email persistence failures and keep auth flow uninterrupted.
  }
}
