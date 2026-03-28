import { router } from "expo-router";
import { useEffect, useState } from "react";
import { Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { GlassCard } from "../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { resetGoogleOAuthFlowState } from "../../src/features/auth/googleOAuthFlowState";
import { useGoogleSignIn } from "../../src/features/auth/useGoogleSignIn";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../src/theme/tokens";

export default function AuthEntryScreen() {
  const { sessionMessage, clearSessionMessage, isAuthTransitioning } = useAuthSession();
  const { playSuccess } = useFeedbackSound();
  const googleSignIn = useGoogleSignIn();
  const [googleError, setGoogleError] = useState<string | null>(null);

  useEffect(() => {
    resetGoogleOAuthFlowState("auth_screen_mount");
  }, []);

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

    playSuccess();
    router.replace("/(tabs)");
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


