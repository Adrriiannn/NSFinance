import * as LocalAuthentication from "expo-local-authentication";
import * as SecureStore from "expo-secure-store";

const BIOMETRIC_PREFERENCE_KEY = "nsfinance.auth.biometric.preference";

export type BiometricPreference = {
  userId: string;
  decision: "enabled" | "declined";
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
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as BiometricPreference;
    if (parsed.userId !== userId || !["enabled", "declined"].includes(parsed.decision)) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

export async function writeBiometricPreference(preference: BiometricPreference) {
  await SecureStore.setItemAsync(BIOMETRIC_PREFERENCE_KEY, JSON.stringify(preference));
}

export async function authenticateWithBiometrics(promptMessage: string) {
  return LocalAuthentication.authenticateAsync({
    promptMessage,
    cancelLabel: "Use another method",
    disableDeviceFallback: true,
    biometricsSecurityLevel: "strong"
  });
}
