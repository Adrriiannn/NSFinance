import { Linking, Pressable, Text } from "react-native";
import { Card } from "../../../src/components/ui/cards/Card";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { HeaderShell } from "../../../src/layout/appHeader";
import { appMetadata } from "../../../src/lib/config/appMetadata";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

export default function AboutScreen() {
  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <HeaderShell preset="secondaryDetail" title="About" />

      <Card style={styles.card}>
        <Text style={styles.title}>NSFinance</Text>
        <Text style={styles.body}>
          NSFinance is a bank-linked personal finance app focused on account clarity, spending analysis, and cash-flow awareness.
        </Text>
        <Text style={styles.meta}>Version: {appMetadata.version}</Text>
      </Card>

      <Card style={styles.card}>
        <Text style={styles.sectionTitle}>Trust notes</Text>
        <Text style={styles.body}>Bank connections are powered by TrueLayer.</Text>
        <Text style={styles.body}>NSFinance does not store banking credentials.</Text>
        <Text style={styles.body}>AI insights are informational only, not financial advice.</Text>
      </Card>

      <Card style={styles.card}>
        <Text style={styles.sectionTitle}>Contact</Text>
        <Pressable
          onPress={() => {
            void Linking.openURL("https://nsireland.ie");
          }}
        >
          <Text style={styles.linkText}>Website: https://nsireland.ie</Text>
        </Pressable>
        <Text style={styles.body}>Support: available in the Support page.</Text>
      </Card>
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



