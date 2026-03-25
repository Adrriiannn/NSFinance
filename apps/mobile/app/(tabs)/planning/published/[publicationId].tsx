import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { Alert, StyleSheet, Text, View } from "react-native";
import { PlanningHubScreen } from "../../../../src/components/planningHub/PlanningHubScreen";
import { EmptyState } from "../../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../../src/components/ui/PrimaryButton";
import { useExpensePlanning } from "../../../../src/features/expenseTracker/ExpensePlanningProvider";
import { palette, spacing, typography } from "../../../../src/theme/tokens";

function formatAmount(amount: number) {
  return new Intl.NumberFormat("en-GB", { style: "currency", currency: "EUR" }).format(amount);
}

function formatDate(value: string | null) {
  if (!value) {
    return "Draft";
  }

  return new Date(value).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

export default function ExpensePlanPublicationDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ publicationId?: string }>();
  const publicationId = typeof params.publicationId === "string" ? params.publicationId : "";
  const { getPublicationById, togglePublicationLike, usePublication: importPublication, unpublishPublication, rescanPublication } = useExpensePlanning();

  const publication = getPublicationById(publicationId);

  if (!publication) {
    return (
      <PlanningHubScreen title="Published plan detail">
        <EmptyState title="Public plan not found" message="This publication is missing or no longer public." />
      </PlanningHubScreen>
    );
  }

  const isCreator = publication.creatorTag === "@you" || publication.creatorName === "You";

  return (
    <PlanningHubScreen title="Published plan detail">
      <GlassCard style={styles.heroCard}>
        <Text style={styles.planTitle}>{publication.publicTitle}</Text>
        <Text style={styles.planMeta}>{publication.creatorName} {publication.creatorTag} • {publication.planType} • {formatDate(publication.publishedAtUtc)}</Text>
        <Text style={styles.planDescription}>{publication.publicDescription}</Text>
        <View style={styles.metricRow}>
          <Text style={styles.metricPill}>{publication.likeCount} likes</Text>
          <Text style={styles.metricPill}>{publication.downloadCount} uses</Text>
          <Text style={styles.metricPill}>{publication.reportCount} reports</Text>
          {publication.isTemplate ? <Text style={styles.metricPill}>Template</Text> : null}
        </View>
      </GlassCard>

      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Plan structure</Text>
        <Text style={styles.sectionCaption}>Expected spend {formatAmount(publication.expectedSpendTotal)}</Text>
        <View style={styles.lineItemList}>
          {publication.lineItems.map((item) => (
            <View key={item.id} style={styles.lineItemRow}>
              <View style={styles.lineItemCopy}>
                <Text style={styles.lineItemTitle}>{item.subcategoryId ? `Category ${item.subcategoryId}` : "Unassigned"}</Text>
                <Text style={styles.lineItemMeta}>{item.notes || "Canonical taxonomy line item"}</Text>
              </View>
              <Text style={styles.lineItemAmount}>{formatAmount(item.expectedAmount)}</Text>
            </View>
          ))}
        </View>
      </GlassCard>

      <GlassCard style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Actions</Text>
        <View style={styles.actionStack}>
          <PrimaryButton label="Like" onPress={() => togglePublicationLike(publication.id)} />
          <PrimaryButton label="Use this plan" onPress={() => {
            const imported = importPublication(publication.id);
            if (!imported) {
              Alert.alert("Could not use plan", "This plan is not available right now.");
              return;
            }
            router.push(`/(tabs)/planning/${imported.id}` as never);
          }} />
          <PrimaryButton label="Report plan" onPress={() => router.push({ pathname: "/(tabs)/planning/published/report", params: { publicationId: publication.id } } as never)} />
        </View>
      </GlassCard>

      {isCreator ? (
        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Creator controls</Text>
          <Text style={styles.sectionCaption}>See moderation feedback, tune metadata, or pull this plan back from the community.</Text>
          <View style={styles.actionStack}>
            <PrimaryButton label="Edit public metadata" onPress={() => router.push({ pathname: "/(tabs)/planning/publish", params: { publicationId: publication.id } } as never)} />
            <PrimaryButton label="Rescan moderation" onPress={() => {
              rescanPublication(publication.id);
              Alert.alert("Moderation refreshed", "The publication was rescanned using the latest local moderation rules.");
            }} />
            <PrimaryButton label="Unpublish" onPress={() => {
              unpublishPublication(publication.id);
              router.replace("/(tabs)/planning/my-published" as never);
            }} />
          </View>
        </GlassCard>
      ) : null}

      {publication.moderationEvents.length > 0 ? (
        <GlassCard style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>Moderation timeline</Text>
          <View style={styles.eventList}>
            {publication.moderationEvents.slice(0, 4).map((event) => (
              <View key={event.id} style={styles.eventRow}>
                <View style={styles.eventCopy}>
                  <Text style={styles.eventTitle}>{event.resultStatus}</Text>
                  <Text style={styles.eventMeta}>{event.summary}</Text>
                </View>
                <Text style={styles.eventDate}>{formatDate(event.createdAtUtc)}</Text>
              </View>
            ))}
          </View>
        </GlassCard>
      ) : null}
    </PlanningHubScreen>
  );
}

const styles = StyleSheet.create({
  heroCard: {
    gap: spacing[12]
  },
  planTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  planMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  planDescription: {
    color: palette.textSecondary,
    ...typography.body2
  },
  metricRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  metricPill: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
  },
  sectionCard: {
    gap: spacing[12]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  sectionCaption: {
    color: palette.textSecondary,
    ...typography.body2
  },
  lineItemList: {
    gap: spacing[12]
  },
  lineItemRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  lineItemCopy: {
    flex: 1,
    gap: 2
  },
  lineItemTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  lineItemMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  lineItemAmount: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  actionStack: {
    gap: spacing[12]
  },
  eventList: {
    gap: spacing[8]
  },
  eventRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  eventCopy: {
    flex: 1,
    gap: 2
  },
  eventTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  eventMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  eventDate: {
    color: palette.textSecondary,
    ...typography.caption
  }
});






