import Ionicons from "@expo/vector-icons/Ionicons";
import { router } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { AppState, Modal, Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { shouldAutoPromptBiometric } from "../../features/auth/sessionProtectionPolicy";
import { useAuthSession } from "../../providers/AuthProvider";
import { Button } from "../ui/buttons/Button";
import { palette, spacing, surfaces, typography, zIndex } from "../../theme/tokens";

export function BiometricGate() {
  const {
    isAppLocked,
    biometricAvailable,
    biometricLabel,
    shouldOfferBiometrics,
    shouldReviewBiometricAfterFallback,
    allowAutomaticBiometricPrompt,
    lockedAccountDisplayName,
    unlockWithBiometrics,
    enableBiometrics,
    disableBiometrics,
    declineBiometrics,
    keepBiometricsAfterFallback,
    signInAnotherWay
  } = useAuthSession();
  const insets = useSafeAreaInsets();
  const automaticAttemptRef = useRef(false);
  const [isWorking, setIsWorking] = useState(false);
  const [isForeground, setIsForeground] = useState(AppState.currentState === "active");
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
    const subscription = AppState.addEventListener("change", (nextState) => {
      setIsForeground(nextState === "active");
    });

    return () => subscription.remove();
  }, []);

  useEffect(() => {
    if (!isAppLocked) {
      automaticAttemptRef.current = false;
      setMessage(null);
      return;
    }

    if (!shouldAutoPromptBiometric({
      isLocked: isAppLocked,
      biometricAvailable,
      isForeground,
      alreadyAttempted: automaticAttemptRef.current
    }) || !allowAutomaticBiometricPrompt) {
      return;
    }

    automaticAttemptRef.current = true;
    void runUnlock();
  }, [allowAutomaticBiometricPrompt, biometricAvailable, isAppLocked, isForeground, runUnlock]);

  const returnToSignIn = async () => {
    await signInAnotherWay();
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

  if (!isAppLocked && !shouldOfferBiometrics && !shouldReviewBiometricAfterFallback) {
    return null;
  }

  if (!isAppLocked && !shouldOfferBiometrics && shouldReviewBiometricAfterFallback) {
    return (
      <Modal
        visible
        transparent
        animationType="fade"
        onRequestClose={() => void keepBiometricsAfterFallback()}
      >
        <Pressable style={styles.reviewOverlay} onPress={() => void keepBiometricsAfterFallback()}>
          <Pressable style={styles.reviewSheet} onPress={() => undefined}>
            <View style={styles.reviewIcon}>
              <Ionicons name="finger-print" size={30} color={palette.primary} />
            </View>
            <Text style={styles.reviewTitle}>Keep fingerprint unlock?</Text>
            <Text style={styles.reviewBody}>
              You signed in another way. Fingerprint unlock can remain ready for your next app launch.
            </Text>
            <View style={styles.reviewActions}>
              <Button
                label="Keep fingerprint"
                onPress={() => void keepBiometricsAfterFallback()}
              />
              <Button
                label="Turn off fingerprint"
                variant="ghost"
                onPress={() => void disableBiometrics()}
              />
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    );
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
        <View style={styles.intro}>
          <View style={styles.iconWrap}>
            <Ionicons
              name={friendlyLabel === "fingerprint" ? "finger-print" : "shield-checkmark-outline"}
              size={48}
              color={palette.primary}
            />
          </View>

          <View style={styles.copy}>
            <Text style={styles.title}>{isOffer ? "Remember this device?" : "Welcome back!"}</Text>
            <Text style={styles.body}>
              {isOffer
                ? `Stay signed in on this phone and use your ${friendlyLabel} to unlock NSFinance next time. Your password is never stored.`
                : `Use your ${friendlyLabel} to log back into your account.`}
            </Text>
            {!isOffer && lockedAccountDisplayName ? (
              <Text style={styles.accountName}>{lockedAccountDisplayName}</Text>
            ) : null}
            {message ? <Text style={styles.error}>{message}</Text> : null}
          </View>
        </View>

        <View style={styles.actions}>
          <Button
            label={isOffer ? `Remember with ${friendlyLabel}` : `Unlock with ${friendlyLabel}`}
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
          ) : (
            <Button label="Sign in another way" variant="ghost" onPress={() => void returnToSignIn()} />
          )}
        </View>

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
    flex: 1
  },
  intro: {
    alignItems: "center",
    paddingTop: spacing[40],
    gap: spacing[20]
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
  accountName: {
    color: palette.textMuted,
    fontSize: typography.helper.fontSize,
    lineHeight: typography.helper.lineHeight,
    fontFamily: typography.helper.fontFamily,
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
    marginTop: "auto",
    gap: spacing[12]
  },
  reviewOverlay: {
    flex: 1,
    justifyContent: "flex-end",
    backgroundColor: palette.overlay
  },
  reviewSheet: {
    borderTopLeftRadius: 8,
    borderTopRightRadius: 8,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    paddingHorizontal: spacing[20],
    paddingTop: spacing[24],
    paddingBottom: spacing[24],
    gap: spacing[12]
  },
  reviewIcon: {
    width: 48,
    height: 48,
    alignItems: "center",
    justifyContent: "center"
  },
  reviewTitle: {
    color: palette.textPrimary,
    fontSize: typography.title2.fontSize,
    lineHeight: typography.title2.lineHeight,
    fontFamily: typography.title2.fontFamily
  },
  reviewBody: {
    color: palette.textSecondary,
    fontSize: typography.body.fontSize,
    lineHeight: typography.body.lineHeight,
    fontFamily: typography.body.fontFamily
  },
  reviewActions: {
    gap: spacing[8],
    paddingTop: spacing[4]
  }
});
