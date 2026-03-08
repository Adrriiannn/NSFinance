import * as SecureStore from "expo-secure-store";

const SAVE_DECISION_KEY = "nsfintech.auth.save_credentials.decision";
const SAVED_CREDENTIALS_KEY = "nsfintech.auth.save_credentials.data";

export type SavedCredentials = {
  email: string;
  password: string;
};

export async function getSavedCredentialsDecision(): Promise<boolean | null> {
  try {
    const raw = await SecureStore.getItemAsync(SAVE_DECISION_KEY);
    if (raw === null) {
      return null;
    }

    return raw === "true";
  } catch {
    return null;
  }
}

export async function setSavedCredentialsDecision(value: boolean): Promise<void> {
  await SecureStore.setItemAsync(SAVE_DECISION_KEY, value ? "true" : "false");
}

export async function getSavedCredentials(): Promise<SavedCredentials | null> {
  try {
    const raw = await SecureStore.getItemAsync(SAVED_CREDENTIALS_KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as SavedCredentials;
    if (!parsed.email || !parsed.password) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

export async function setSavedCredentials(value: SavedCredentials): Promise<void> {
  await SecureStore.setItemAsync(SAVED_CREDENTIALS_KEY, JSON.stringify(value));
}

export async function clearSavedCredentials(): Promise<void> {
  await SecureStore.deleteItemAsync(SAVED_CREDENTIALS_KEY);
}
