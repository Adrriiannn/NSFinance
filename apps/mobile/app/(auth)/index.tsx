import { Ionicons } from "@expo/vector-icons";
import { router } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { GlassCard } from "../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function AuthEntryScreen() {
  const { sessionMessage, clearSessionMessage } = useAuthSession();

  return (
    <AuthScreen>
      <View style={styles.hero}>
        <Text style={styles.brand}>NSFinTech</Text>
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
        />

        <SecondaryButton
          label="Create account"
          onPress={() => {
            clearSessionMessage();
            router.push("/register" as never);
          }}
        />

        <SecondaryButton
          label="Forgot password"
          onPress={() => {
            clearSessionMessage();
            router.push("/forgot-password" as never);
          }}
        />

        <SecondaryButton
          label="Verify email"
          onPress={() => {
            clearSessionMessage();
            router.push("/verify-email" as never);
          }}
        />

        <SecondaryButton
          label="Sign in with Google"
          onPress={() => clearSessionMessage()}
          disabled
        />
        <View style={styles.googleHint}>
          <Ionicons name="logo-google" size={16} color={palette.textSecondary} />
          <Text style={styles.googleHintText}>Google sign-in coming soon</Text>
        </View>

        <View style={styles.legalRow}>
          <SecondaryButton label="Terms" onPress={() => router.push("/legal/terms" as never)} />
          <SecondaryButton label="Privacy" onPress={() => router.push("/legal/privacy" as never)} />
        </View>
      </GlassCard>
    </AuthScreen>
  );
}

const styles = StyleSheet.create({
  hero: {
    marginTop: spacing[20],
    marginBottom: spacing[24],
    gap: spacing[12]
  },
  brand: {
    color: palette.accent,
    ...typography.caption,
    fontWeight: "700",
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
  googleHint: {
    marginTop: spacing[4],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  googleHintText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  legalRow: {
    marginTop: spacing[4],
    flexDirection: "row",
    gap: spacing[8]
  }
});
