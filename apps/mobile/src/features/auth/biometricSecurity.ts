import * as LocalAuthentication from "expo-local-authentication";
import * as SecureStore from "expo-secure-store";
import {
  parseBiometricPreferenceStore,
  setBiometricPreference
} from "./biometricPreferencePolicy";

const BIOMETRIC_PREFERENCE_KEY = "nsfinance.auth.biometric.preference";

export type BiometricPreference = {
  userId: string;
  decision: "enabled" | "declined";
  fallbackReviewDismissed?: boolean;
};

export type BiometricAvailability = {
  available: boolean;
  label: string;
};

export async function getBiometricAvailability(): Promise<BiometricAvailability> {
  try {
    const [hasHardware, isEnrolled, supportedTypes] = await Promise.all([
      LocalAuthentication.hasHardwareAsync(),
      LocalAuthentication.isEnrolledAsync(),
      LocalAuthentication.supportedAuthenticationTypesAsync()
    ]);
    const hasFingerprint = supportedTypes.includes(
      LocalAuthentication.AuthenticationType.FINGERPRINT
    );

    return {
      available: hasHardware && isEnrolled && supportedTypes.length > 0,
      label: hasFingerprint ? "fingerprint" : "biometrics"
    };
  } catch {
    return { available: false, label: "biometrics" };
  }
}

export async function readBiometricPreference(
  userId: string
): Promise<BiometricPreference | null> {
  try {
    const raw = await SecureStore.getItemAsync(BIOMETRIC_PREFERENCE_KEY);
    return parseBiometricPreferenceStore(raw).preferences[userId] ?? null;
  } catch {
    return null;
  }
}

export async function writeBiometricPreference(preference: BiometricPreference) {
  const raw = await SecureStore.getItemAsync(BIOMETRIC_PREFERENCE_KEY);
  const store = setBiometricPreference(parseBiometricPreferenceStore(raw), preference);
  await SecureStore.setItemAsync(BIOMETRIC_PREFERENCE_KEY, JSON.stringify(store));
}

export async function authenticateWithBiometrics({
  promptMessage,
  promptDescription,
  cancelLabel
}: {
  promptMessage: string;
  promptDescription: string;
  cancelLabel: string;
}) {
  return LocalAuthentication.authenticateAsync({
    promptMessage,
    promptDescription,
    cancelLabel,
    disableDeviceFallback: true,
    biometricsSecurityLevel: "strong",
    requireConfirmation: true
  });
}
