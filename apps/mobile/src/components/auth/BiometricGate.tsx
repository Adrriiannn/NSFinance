import Ionicons from "@expo/vector-icons/Ionicons";
import { router } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { AppState, Modal, Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import {
  shouldAutoPromptBiometric,
  shouldAutoStartRememberedMfa
} from "../../features/auth/sessionProtectionPolicy";
import { stageMfaLogin } from "../../features/auth/pendingAuthFlow";
import { useAuthSession } from "../../providers/AuthProvider";
import { Button } from "../ui/buttons/Button";
import { palette, spacing, surfaces, typography, zIndex } from "../../theme/tokens";

export function BiometricGate() {
  const {
    isAppLocked,
    biometricAvailable,
    biometricLabel,
    shouldOfferBiometrics,
    requiresRememberProtectionSetup,
    shouldReviewBiometricAfterFallback,
    allowAutomaticBiometricPrompt,
    rememberedUnlockMethod,
    canUseRememberedSessionMfa,
    rememberedAccountHint,
    unlockWithBiometrics,
    beginRememberedSessionMfa,
    enableBiometrics,
    disableBiometrics,
    declineBiometrics,
    keepBiometricsAfterFallback,
    continueWithoutRemembering,
    openRememberProtectionSetup,
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

  const runAuthenticator = useCallback(async () => {
    if (isWorking) {
      return;
    }

    setIsWorking(true);
    setMessage(null);
    const result = await beginRememberedSessionMfa();
    if (!result.succeeded || !result.challenge) {
      setMessage(result.message ?? "The Authenticator check could not be started.");
      setIsWorking(false);
      return;
    }

    stageMfaLogin({
      ...result.challenge,
      context: "remembered_session",
      rememberSession: true
    });
    setIsWorking(false);
    router.replace("/(auth)/mfa" as never);
  }, [beginRememberedSessionMfa, isWorking]);

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
    }) || !allowAutomaticBiometricPrompt || rememberedUnlockMethod !== "biometric") {
      return;
    }

    automaticAttemptRef.current = true;
    void runUnlock();
  }, [
    allowAutomaticBiometricPrompt,
    biometricAvailable,
    isAppLocked,
    isForeground,
    rememberedUnlockMethod,
    runUnlock
  ]);

  useEffect(() => {
    if (!shouldAutoStartRememberedMfa({
      isLocked: isAppLocked,
      unlockMethod: rememberedUnlockMethod,
      isForeground,
      alreadyAttempted: automaticAttemptRef.current
    }) || !allowAutomaticBiometricPrompt) {
      return;
    }

    automaticAttemptRef.current = true;
    void runAuthenticator();
  }, [
    allowAutomaticBiometricPrompt,
    isAppLocked,
    isForeground,
    rememberedUnlockMethod,
    runAuthenticator
  ]);

  const returnToSignIn = async () => {
    if (canUseRememberedSessionMfa) {
      await runAuthenticator();
      return;
    }

    await signInAnotherWay();
    router.replace("/(auth)/login" as never);
  };

  const chooseDifferentAccount = async () => {
    await signInAnotherWay();
    router.replace("/(auth)/login" as never);
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

  if (
    !isAppLocked
    && !shouldOfferBiometrics
    && !requiresRememberProtectionSetup
    && !shouldReviewBiometricAfterFallback
  ) {
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

  if (!isAppLocked && requiresRememberProtectionSetup) {
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
              <Ionicons name="shield-checkmark-outline" size={48} color={palette.primary} />
            </View>
            <View style={styles.copy}>
              <Text style={styles.title}>Protect remembered sign-in</Text>
              <Text style={styles.body}>
                Remember me needs fingerprint or Authenticator protection on this device. Your
                current session can continue without being stored.
              </Text>
            </View>
          </View>
          <View style={styles.actions}>
            <Button
              label="Set up Authenticator"
              onPress={() => {
                openRememberProtectionSetup();
                router.push("/(tabs)/accounts/security" as never);
              }}
            />
            <Button
              label="Continue without remembering"
              variant="ghost"
              onPress={() => void continueWithoutRemembering()}
            />
          </View>
        </View>
      </View>
    );
  }

  const isOffer = !isAppLocked && shouldOfferBiometrics;
  const isMfaUnlock = isAppLocked && rememberedUnlockMethod === "mfa";
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
            <Text style={styles.title}>{isOffer ? "Set up fingerprint unlock" : "Welcome back!"}</Text>
            {isOffer ? (
              <View style={styles.bodyStack}>
                <Text style={styles.body}>You selected to remember this account.</Text>
                <Text style={styles.body}>
                  Use your {friendlyLabel} to protect this account on this device.
                </Text>
                <Text style={styles.body}>Your password is never stored.</Text>
              </View>
            ) : (
              <Text style={styles.body}>
                {isMfaUnlock
                  ? "Use Authenticator to log back into your account."
                  : `Use your ${friendlyLabel} to log back into your account.`}
              </Text>
            )}
            {!isOffer && rememberedAccountHint ? (
              <Text style={styles.accountHint}>{rememberedAccountHint}</Text>
            ) : null}
            {message ? <Text style={styles.error}>{message}</Text> : null}
          </View>
        </View>

        <View style={styles.actions}>
          <Button
            label={
              isOffer
                ? `Set up ${friendlyLabel}`
                : isMfaUnlock
                  ? "Use Authenticator"
                  : `Unlock with ${friendlyLabel}`
            }
            onPress={() => void (isOffer ? enable() : isMfaUnlock ? runAuthenticator() : runUnlock())}
            disabled={!isMfaUnlock && !biometricAvailable}
            isLoading={isWorking}
            icon={
              isWorking ? undefined : (
                <Ionicons
                  name={isMfaUnlock ? "keypad-outline" : "finger-print"}
                  size={20}
                  color={palette.appBackground}
                />
              )
            }
          />
          {isOffer ? (
            <Button
              label={canUseRememberedSessionMfa ? "Use Authenticator instead" : "Continue without remembering"}
              variant="ghost"
              onPress={() => void declineBiometrics()}
            />
          ) : (
            <>
              {!isMfaUnlock && canUseRememberedSessionMfa ? (
                <Button
                  label="Use Authenticator"
                  variant="ghost"
                  onPress={() => void returnToSignIn()}
                />
              ) : null}
              <Button
                label="Use a different account"
                variant="ghost"
                onPress={() => void chooseDifferentAccount()}
              />
            </>
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
  bodyStack: {
    alignItems: "center",
    gap: spacing[4]
  },
  accountHint: {
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
