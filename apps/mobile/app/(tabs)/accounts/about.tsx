import { Linking, Pressable, Text } from "react-native";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { HeaderShell } from "../../../src/layout/appHeader";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

export default function AboutScreen() {
  const appVersion = process.env.EXPO_PUBLIC_APP_VERSION || "0.1.0";
  const environment = process.env.EXPO_PUBLIC_APP_ENV || "development";

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <HeaderShell preset="secondaryDetail" title="About" />

      <GlassCard style={styles.card}>
        <Text style={styles.title}>NSFinance</Text>
        <Text style={styles.body}>
          NSFinance is a bank-linked personal finance app focused on account clarity, spending analysis, and planning insights.
        </Text>
        <Text style={styles.meta}>Version: {appVersion}</Text>
        <Text style={styles.meta}>Environment: {environment}</Text>
        <Text style={styles.meta}>Operator details will be published here before public launch.</Text>
        <Text style={styles.meta}>Legal entity name: pending publication</Text>
        <Text style={styles.meta}>Registered address: pending publication</Text>
        <Text style={styles.meta}>Support/privacy contact: pending publication</Text>
      </GlassCard>

      <GlassCard style={styles.card}>
        <Text style={styles.sectionTitle}>Trust notes</Text>
        <Text style={styles.body}>Bank connections are powered by TrueLayer.</Text>
        <Text style={styles.body}>NSFinance does not store banking credentials.</Text>
        <Text style={styles.body}>AI insights are informational only, not financial advice.</Text>
      </GlassCard>

      <GlassCard style={styles.card}>
        <Text style={styles.sectionTitle}>Contact</Text>
        <Pressable
          onPress={() => {
            void Linking.openURL("https://nsireland.ie");
          }}
        >
          <Text style={styles.linkText}>Website: https://nsireland.ie</Text>
        </Pressable>
        <Text style={styles.body}>Support: available in the Support page.</Text>
      </GlassCard>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    gap: spacing[12]
  },
  card: {
    gap: spacing[8]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  body: {
    color: palette.textSecondary,
    ...typography.body2
  },
  linkText: {
    color: palette.primaryGlow,
    ...typography.body2
  },
  meta: {
    color: palette.primaryGlow,
    ...typography.caption
  }
}));



