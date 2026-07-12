import { router } from "expo-router";
import { useState } from "react";
import { Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { GlassCard } from "../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { useGoogleSignIn } from "../../src/features/auth/useGoogleSignIn";
import { useMicrosoftSignIn } from "../../src/features/auth/useMicrosoftSignIn";
import { stageEmailVerification, stageMfaLogin } from "../../src/features/auth/pendingAuthFlow";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../src/theme/tokens";

export default function AuthEntryScreen() {
  const { applyAuthTokenResponse, sessionMessage, clearSessionMessage, isAuthTransitioning } = useAuthSession();
  const { playSuccess } = useFeedbackSound();
  const googleSignIn = useGoogleSignIn();
  const microsoftSignIn = useMicrosoftSignIn();
  const [googleError, setGoogleError] = useState<string | null>(null);

  const handleGoogleSignIn = async () => {
    if (isAuthTransitioning) {
      setGoogleError("Finishing sign-out. Please try again in a moment.");
      return;
    }

    clearSessionMessage();
    setGoogleError(null);

    const result = await googleSignIn.signInWithGoogle();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setGoogleError(result.message ?? "Google sign-in failed.");
      }
      return;
    }

    const flow = result.flow;
    if (flow?.status === "authenticated" && flow.session) {
      await applyAuthTokenResponse(flow.session);
      playSuccess();
      router.replace("/(tabs)");
      return;
    }

    if (flow?.status === "email_verification_required" && flow.emailVerification) {
      stageEmailVerification(flow.emailVerification);
      router.push("/(auth)/verify-email" as never);
      return;
    }

    if (flow?.status === "mfa_required" && flow.mfaChallenge) {
      stageMfaLogin(flow.mfaChallenge);
      router.push("/(auth)/mfa" as never);
      return;
    }

    setGoogleError("Google sign-in returned an incomplete response. Please try again.");
  };

  const handleMicrosoftSignIn = async () => {
    if (isAuthTransitioning) {
      setGoogleError("Finishing sign-out. Please try again in a moment.");
      return;
    }

    clearSessionMessage();
    setGoogleError(null);
    const result = await microsoftSignIn.signInWithMicrosoft();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setGoogleError(result.message ?? "Microsoft sign-in failed.");
      }
      return;
    }

    const flow = result.flow;
    if (flow?.status === "authenticated" && flow.session) {
      await applyAuthTokenResponse(flow.session);
      playSuccess();
      router.replace("/(tabs)");
      return;
    }

    if (flow?.status === "email_verification_required" && flow.emailVerification) {
      stageEmailVerification(flow.emailVerification);
      router.push("/(auth)/verify-email" as never);
      return;
    }

    if (flow?.status === "mfa_required" && flow.mfaChallenge) {
      stageMfaLogin(flow.mfaChallenge);
      router.push("/(auth)/mfa" as never);
      return;
    }

    setGoogleError("Microsoft sign-in returned an incomplete response. Please try again.");
  };

  return (
    <AuthScreen>
      <View style={styles.hero}>
        <Text style={styles.brand}>NSFinance</Text>
        <Text style={styles.title}>Your calm finance companion</Text>
        <Text style={styles.subtitle}>
          Track accounts, monitor spending, and stay clear on what matters today.
        </Text>
      </View>

      <GlassCard style={styles.card}>
        {sessionMessage ? <Text style={styles.sessionMessage}>{sessionMessage}</Text> : null}

        <PrimaryButton
          label="Sign in"
          onPress={() => {
            clearSessionMessage();
            router.push("/login" as never);
          }}
          disabled={isAuthTransitioning}
        />

        <SecondaryButton
          label="Create account"
          onPress={() => {
            clearSessionMessage();
            router.push("/register" as never);
          }}
          disabled={isAuthTransitioning}
        />

        <SecondaryButton
          label="Forgot password"
          onPress={() => {
            clearSessionMessage();
            router.push("/forgot-password" as never);
          }}
          disabled={isAuthTransitioning}
        />

        <SecondaryButton
          label={googleSignIn.isPending ? "Signing in with Google..." : "Sign in with Google"}
          onPress={() => void handleGoogleSignIn()}
          disabled={!googleSignIn.isConfigured || googleSignIn.isPending || isAuthTransitioning}
        />
        <SecondaryButton
          label={microsoftSignIn.isPending ? "Signing in with Microsoft..." : "Sign in with Microsoft"}
          onPress={() => void handleMicrosoftSignIn()}
          disabled={!microsoftSignIn.isReady || microsoftSignIn.isPending || isAuthTransitioning}
        />
        {googleError ? <Text style={styles.googleError}>{googleError}</Text> : null}

        <View style={styles.legalRow}>
          <SecondaryButton label="Terms" onPress={() => router.push("/legal/terms" as never)} />
          <SecondaryButton label="Privacy" onPress={() => router.push("/legal/privacy-policy" as never)} />
        </View>
      </GlassCard>
    </AuthScreen>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  hero: {
    marginTop: spacing[20],
    marginBottom: spacing[24],
    gap: spacing[12]
  },
  brand: {
    color: palette.accent,
    ...typography.caption,
    fontWeight: "600",
    letterSpacing: 1.2
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.body2
  },
  card: {
    gap: spacing[12]
  },
  sessionMessage: {
    color: palette.caution,
    ...typography.caption
  },
  googleError: {
    color: palette.negative,
    ...typography.caption
  },
  legalRow: {
    marginTop: spacing[4],
    flexDirection: "row",
    gap: spacing[8]
  }
}));


