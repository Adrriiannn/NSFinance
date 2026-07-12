import Ionicons from "@expo/vector-icons/Ionicons";
import { router } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAuthSession } from "../../providers/AuthProvider";
import { Button } from "../ui/buttons/Button";
import { palette, spacing, surfaces, typography, zIndex } from "../../theme/tokens";

export function BiometricGate() {
  const {
    isAppLocked,
    biometricAvailable,
    biometricLabel,
    biometricFailureCount,
    shouldOfferBiometrics,
    session,
    unlockWithBiometrics,
    enableBiometrics,
    declineBiometrics,
    logout
  } = useAuthSession();
  const insets = useSafeAreaInsets();
  const automaticAttemptRef = useRef(false);
  const [isWorking, setIsWorking] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const runUnlock = useCallback(async () => {
    if (isWorking) {
      return;
    }

    setIsWorking(true);
    setMessage(null);
    const result = await unlockWithBiometrics();
    if (!result.succeeded && result.message) {
      setMessage(result.message);
    }
    setIsWorking(false);
  }, [isWorking, unlockWithBiometrics]);

  useEffect(() => {
    if (!isAppLocked) {
      automaticAttemptRef.current = false;
      setMessage(null);
      return;
    }

    if (!biometricAvailable || automaticAttemptRef.current) {
      return;
    }

    automaticAttemptRef.current = true;
    void runUnlock();
  }, [biometricAvailable, isAppLocked, runUnlock]);

  const returnToPasswordSignIn = async () => {
    await logout();
    router.replace("/(auth)/login");
  };

  const enable = async () => {
    if (isWorking) {
      return;
    }

    setIsWorking(true);
    setMessage(null);
    const result = await enableBiometrics();
    if (!result.succeeded && result.message) {
      setMessage(result.message);
    }
    setIsWorking(false);
  };

  if (!isAppLocked && !shouldOfferBiometrics) {
    return null;
  }

  const isOffer = !isAppLocked && shouldOfferBiometrics;
  const friendlyLabel = biometricLabel === "fingerprint" ? "fingerprint" : "biometrics";

  return (
    <View
      accessibilityViewIsModal
      style={[
        styles.overlay,
        { paddingTop: insets.top + spacing[24], paddingBottom: insets.bottom + spacing[24] }
      ]}
    >
      <View style={styles.content}>
        <View style={styles.iconWrap}>
          <Ionicons
            name={friendlyLabel === "fingerprint" ? "finger-print" : "shield-checkmark-outline"}
            size={42}
            color={palette.primary}
          />
        </View>

        <View style={styles.copy}>
          <Text style={styles.title}>{isOffer ? "Unlock faster next time" : "Welcome back"}</Text>
          <Text style={styles.body}>
            {isOffer
              ? `Use your ${friendlyLabel} before NSFinance shows your financial information.`
              : `Use your ${friendlyLabel} to unlock ${session?.user.displayName ? `${session.user.displayName}'s` : "your"} account.`}
          </Text>
          {message ? <Text style={styles.error}>{message}</Text> : null}
        </View>

        <View style={styles.actions}>
          <Button
            label={isOffer ? `Use ${friendlyLabel}` : `Unlock with ${friendlyLabel}`}
            onPress={() => void (isOffer ? enable() : runUnlock())}
            disabled={!biometricAvailable}
            isLoading={isWorking}
            icon={
              isWorking ? undefined : (
                <Ionicons name="finger-print" size={20} color={palette.appBackground} />
              )
            }
          />
          {isOffer ? (
            <Button label="Not now" variant="ghost" onPress={() => void declineBiometrics()} />
          ) : biometricFailureCount >= 5 || !biometricAvailable ? (
            <Button label="Use password instead" variant="ghost" onPress={() => void returnToPasswordSignIn()} />
          ) : (
            <Button label="Use another method" variant="ghost" onPress={() => void returnToPasswordSignIn()} />
          )}
        </View>

        {isWorking ? <ActivityIndicator style={styles.hiddenProgress} color={palette.primary} /> : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  overlay: {
    ...StyleSheet.absoluteFillObject,
    zIndex: zIndex.modal,
    elevation: 100,
    backgroundColor: surfaces.app,
    justifyContent: "center",
    paddingHorizontal: spacing[24]
  },
  content: {
    width: "100%",
    maxWidth: 420,
    alignSelf: "center",
    gap: spacing[32]
  },
  iconWrap: {
    width: 72,
    height: 72,
    alignItems: "center",
    justifyContent: "center",
    alignSelf: "center"
  },
  copy: {
    alignItems: "center",
    gap: spacing[8]
  },
  title: {
    color: palette.textPrimary,
    fontSize: typography.title.fontSize,
    lineHeight: typography.title.lineHeight,
    fontFamily: typography.title.fontFamily,
    textAlign: "center"
  },
  body: {
    color: palette.textSecondary,
    fontSize: typography.body.fontSize,
    lineHeight: typography.body.lineHeight,
    fontFamily: typography.body.fontFamily,
    textAlign: "center"
  },
  error: {
    color: palette.negative,
    fontSize: typography.helper.fontSize,
    lineHeight: typography.helper.lineHeight,
    fontFamily: typography.helper.fontFamily,
    textAlign: "center"
  },
  actions: {
    gap: spacing[12]
  },
  hiddenProgress: {
    position: "absolute",
    opacity: 0
  }
});
