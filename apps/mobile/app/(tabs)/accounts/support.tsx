import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { StyleSheet, Text, View } from "react-native";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SectionHeader } from "../../../src/components/ui/SectionHeader";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";

export default function AccountSupportScreen() {
  const router = useRouter();

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Support</Text>
        <View style={{ width: 42 }} />
      </View>

      <SectionHeader title="FAQ" />
      <GlassCard style={styles.block}>
        <Text style={styles.itemTitle}>How do I connect a bank?</Text>
        <Text style={styles.itemBody}>Bank connection setup will be available from the Connect Bank action soon.</Text>
      </GlassCard>
      <GlassCard style={styles.block}>
        <Text style={styles.itemTitle}>How do I correct transaction context?</Text>
        <Text style={styles.itemBody}>Open a transaction and use the Transaction Context modal to update category and notes.</Text>
      </GlassCard>

      <SectionHeader title="Help categories" />
      <View style={styles.categoryGrid}>
        <GlassCard style={styles.categoryItem}>
          <Text style={styles.categoryText}>Accounts</Text>
        </GlassCard>
        <GlassCard style={styles.categoryItem}>
          <Text style={styles.categoryText}>Transactions</Text>
        </GlassCard>
        <GlassCard style={styles.categoryItem}>
          <Text style={styles.categoryText}>Planner</Text>
        </GlassCard>
        <GlassCard style={styles.categoryItem}>
          <Text style={styles.categoryText}>Security</Text>
        </GlassCard>
      </View>

      <SectionHeader title="Ticketing" />
      <GlassCard style={styles.block}>
        <Text style={styles.itemBody}>Need help from the team? Create a support ticket and include the affected account or transaction.</Text>
        <PrimaryButton label="Create support ticket" onPress={() => undefined} />
      </GlassCard>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding
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
  block: {
    gap: spacing[8]
  },
  itemTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  itemBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  categoryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  categoryItem: {
    width: "48.7%",
    minHeight: 62,
    alignItems: "center",
    justifyContent: "center"
  },
  categoryText: {
    color: palette.textPrimary,
    ...typography.body1
  }
});
