import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { Linking, Pressable, StyleSheet, Text, View } from "react-native";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { palette, spacing, typography } from "../../../src/theme/tokens";

export default function AboutScreen() {
  const router = useRouter();
  const appVersion = process.env.EXPO_PUBLIC_APP_VERSION || "0.1.0";
  const environment = process.env.EXPO_PUBLIC_APP_ENV || "development";

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>About</Text>
        <View style={{ width: 42 }} />
      </View>

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

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[16],
    gap: spacing[12]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
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
});
