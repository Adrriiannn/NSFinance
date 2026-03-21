import { Ionicons } from "@expo/vector-icons";
import { Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import type { ExpensePlanPublication } from "../../../features/expenseTracker/expensePlanningTypes";
import { palette, radius, spacing, typography } from "../../../theme/tokens";

function formatAmount(amount: number) {
  return new Intl.NumberFormat("en-GB", { style: "currency", currency: "EUR" }).format(amount);
}

type FeaturedPlanCarouselProps = {
  items: ExpensePlanPublication[];
  onPressPublication: (publicationId: string) => void;
};

export function FeaturedPlanCarousel({
  items,
  onPressPublication
}: FeaturedPlanCarouselProps) {
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.rail}>
      {items.map((publication) => (
        <Pressable
          key={publication.id}
          style={styles.card}
          onPress={() => onPressPublication(publication.id)}
        >
          <View style={styles.headerRow}>
            <Text style={styles.title} numberOfLines={2}>{publication.publicTitle}</Text>
            <Ionicons name="chevron-forward" size={18} color={palette.textSecondary} />
          </View>

          <Text style={styles.creator}>{publication.creatorName} {publication.creatorTag}</Text>
          <Text style={styles.description} numberOfLines={3}>{publication.publicDescription}</Text>

          <View style={styles.metricRow}>
            <Text style={styles.metricPill}>{publication.likeCount} likes</Text>
            <Text style={styles.metricPill}>{publication.downloadCount} uses</Text>
            {publication.expectedSpendTotal > 0 ? (
              <Text style={styles.metricPill}>{formatAmount(publication.expectedSpendTotal)}</Text>
            ) : null}
          </View>
        </Pressable>
      ))}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  rail: {
    gap: spacing[12],
    paddingRight: spacing[16]
  },
  card: {
    width: 304,
    minHeight: 174,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(109,170,255,0.4)",
    backgroundColor: "rgba(22,45,74,0.72)",
    padding: spacing[16],
    gap: spacing[8]
  },
  headerRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[8],
    alignItems: "flex-start"
  },
  title: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "800"
  },
  creator: {
    color: palette.textSecondary,
    ...typography.caption
  },
  description: {
    color: palette.textSecondary,
    ...typography.body2
  },
  metricRow: {
    marginTop: "auto",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  metricPill: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  }
});

