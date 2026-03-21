import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { Pressable, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import { PlanningHubScreen } from "../../../src/components/planningHub/PlanningHubScreen";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { useExpensePlanning } from "../../../src/features/expenseTracker/ExpensePlanningProvider";
import { buildPublicationSections, searchAndSortExpensePlanPublications } from "../../../src/features/expenseTracker/expensePlanCommunityUtils";
import type { ExpensePlanPeriodType, ExpensePlanPublicationSort } from "../../../src/features/expenseTracker/expensePlanningTypes";
import { palette, radius, spacing, typography } from "../../../src/theme/tokens";

const sortOptions: Array<{ id: ExpensePlanPublicationSort; label: string }> = [
  { id: "trending", label: "Trending" },
  { id: "most_liked", label: "Most liked" },
  { id: "most_downloaded", label: "Most downloaded" },
  { id: "recently_added", label: "Recently added" },
  { id: "newest", label: "Newest" }
];

const typeOptions: Array<{ id: "all" | ExpensePlanPeriodType; label: string }> = [
  { id: "all", label: "All" },
  { id: "weekly", label: "Weekly" },
  { id: "monthly", label: "Monthly" },
  { id: "custom", label: "Custom" }
];

function formatAmount(amount: number) {
  return new Intl.NumberFormat("en-GB", { style: "currency", currency: "EUR" }).format(amount);
}

function formatDate(value: string | null) {
  if (!value) {
    return "Draft";
  }

  return new Date(value).toLocaleDateString("en-GB", { day: "numeric", month: "short" });
}

export default function ExpensePlanCommunityBrowserScreen() {
  const router = useRouter();
  const { publications } = useExpensePlanning();
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<ExpensePlanPublicationSort>("trending");
  const [planType, setPlanType] = useState<"all" | ExpensePlanPeriodType>("all");
  const [templatesOnly, setTemplatesOnly] = useState(false);

  const sections = useMemo(() => buildPublicationSections(publications), [publications]);
  const filtered = useMemo(() => searchAndSortExpensePlanPublications({
    publications,
    search,
    sort,
    planType,
    creatorFilter: "",
    templatesOnly
  }), [planType, publications, search, sort, templatesOnly]);

  return (
    <PlanningHubScreen title="Browse plans">
      <GlassCard style={styles.heroCard}>
        <View style={styles.heroRow}>
          <View style={styles.heroCopy}>
            <Text style={styles.heroTitle}>Community library</Text>
            <Text style={styles.heroBody}>Browse polished public plans, save the ones that fit your season, and publish your own when they’re ready.</Text>
          </View>
          <View style={styles.heroMetricsWrap}>
            <Text style={styles.heroMetric}>{publications.filter((item) => item.publicationStatus === "published").length}</Text>
            <Text style={styles.heroMetricLabel}>Live plans</Text>
          </View>
        </View>
        <View style={styles.heroActions}>
          <Pressable style={styles.heroActionButton} onPress={() => router.push("/(tabs)/planning/publish" as never)}>
            <Ionicons name="share-social-outline" size={16} color={palette.textPrimary} />
            <Text style={styles.heroActionLabel}>Publish a plan</Text>
          </Pressable>
          <Pressable style={styles.heroActionButton} onPress={() => router.push("/(tabs)/planning/my-published" as never)}>
            <Ionicons name="stats-chart-outline" size={16} color={palette.textPrimary} />
            <Text style={styles.heroActionLabel}>My published plans</Text>
          </Pressable>
        </View>
      </GlassCard>

      <View style={styles.searchWrap}>
        <Ionicons name="search-outline" size={18} color={palette.textSecondary} />
        <TextInput
          value={search}
          onChangeText={setSearch}
          placeholder="Search plans, tags, or creators"
          placeholderTextColor={palette.textSecondary}
          style={styles.searchInput}
        />
      </View>

      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.filterRail}>
        {sortOptions.map((option) => (
          <Pressable key={option.id} style={[styles.filterChip, sort === option.id ? styles.filterChipActive : null]} onPress={() => setSort(option.id)}>
            <Text style={[styles.filterChipLabel, sort === option.id ? styles.filterChipLabelActive : null]}>{option.label}</Text>
          </Pressable>
        ))}
      </ScrollView>

      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.filterRail}>
        {typeOptions.map((option) => (
          <Pressable key={option.id} style={[styles.filterChip, planType === option.id ? styles.filterChipActive : null]} onPress={() => setPlanType(option.id)}>
            <Text style={[styles.filterChipLabel, planType === option.id ? styles.filterChipLabelActive : null]}>{option.label}</Text>
          </Pressable>
        ))}
        <Pressable style={[styles.filterChip, templatesOnly ? styles.filterChipActive : null]} onPress={() => setTemplatesOnly((current) => !current)}>
          <Text style={[styles.filterChipLabel, templatesOnly ? styles.filterChipLabelActive : null]}>Templates</Text>
        </Pressable>
      </ScrollView>

      <View style={styles.sectionWrap}>
        <Text style={styles.sectionTitle}>Featured</Text>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.featureRail}>
          {sections.featured.map((publication) => (
            <Pressable key={publication.id} style={styles.featureCard} onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}>
              <Text style={styles.featureTitle}>{publication.publicTitle}</Text>
              <Text style={styles.featureMeta}>{publication.creatorName} • {publication.planType}</Text>
              <Text style={styles.featureBody} numberOfLines={3}>{publication.publicDescription}</Text>
              <View style={styles.metricRow}>
                <Text style={styles.metricPill}>{publication.likeCount} likes</Text>
                <Text style={styles.metricPill}>{publication.downloadCount} uses</Text>
                <Text style={styles.metricPill}>{formatAmount(publication.expectedSpendTotal)}</Text>
              </View>
            </Pressable>
          ))}
        </ScrollView>
      </View>

      <View style={styles.sectionWrap}>
        <Text style={styles.sectionTitle}>Popular this week</Text>
        <View style={styles.stackList}>
          {sections.popularThisWeek.slice(0, 3).map((publication) => (
            <Pressable key={publication.id} style={styles.rowCard} onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}>
              <View style={styles.rowCardCopy}>
                <Text style={styles.rowCardTitle}>{publication.publicTitle}</Text>
                <Text style={styles.rowCardMeta}>{publication.creatorTag} • {publication.likeCount} likes • {publication.downloadCount} uses</Text>
              </View>
              <Ionicons name="chevron-forward" size={18} color={palette.textSecondary} />
            </Pressable>
          ))}
        </View>
      </View>

      <View style={styles.sectionWrap}>
        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>All public plans</Text>
          <Text style={styles.sectionCaption}>{filtered.length} results</Text>
        </View>
        {filtered.length === 0 ? (
          <EmptyState title="No public plans found" message="Try another search, filter, or publish the first plan in this niche." />
        ) : (
          <View style={styles.stackList}>
            {filtered.map((publication) => (
              <Pressable key={publication.id} style={styles.libraryCard} onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}>
                <View style={styles.libraryCardHeader}>
                  <View style={styles.libraryCardCopy}>
                    <Text style={styles.libraryCardTitle}>{publication.publicTitle}</Text>
                    <Text style={styles.libraryCardMeta}>{publication.creatorName} {publication.creatorTag}</Text>
                  </View>
                  <Text style={styles.libraryCardFreshness}>{formatDate(publication.publishedAtUtc)}</Text>
                </View>
                <Text style={styles.libraryCardBody} numberOfLines={2}>{publication.publicDescription}</Text>
                <View style={styles.metricRow}>
                  <Text style={styles.metricPill}>{publication.planType}</Text>
                  <Text style={styles.metricPill}>{publication.likeCount} likes</Text>
                  <Text style={styles.metricPill}>{publication.downloadCount} uses</Text>
                  {publication.isTemplate ? <Text style={styles.metricPill}>Template</Text> : null}
                </View>
              </Pressable>
            ))}
          </View>
        )}
      </View>
    </PlanningHubScreen>
  );
}

const styles = StyleSheet.create({
  heroCard: {
    gap: spacing[16]
  },
  heroRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  heroCopy: {
    flex: 1,
    gap: spacing[8]
  },
  heroTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  heroBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  heroMetricsWrap: {
    minWidth: 72,
    alignItems: "center",
    justifyContent: "center",
    gap: 4
  },
  heroMetric: {
    color: palette.textPrimary,
    ...typography.title1
  },
  heroMetricLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  heroActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[12]
  },
  heroActionButton: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8],
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8],
    borderRadius: radius.medium,
    backgroundColor: "rgba(255,255,255,0.06)"
  },
  heroActionLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  searchWrap: {
    minHeight: 52,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.10)",
    backgroundColor: "rgba(255,255,255,0.04)",
    paddingHorizontal: spacing[16],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  searchInput: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body1
  },
  filterRail: {
    gap: spacing[8],
    paddingRight: spacing[16]
  },
  filterChip: {
    minHeight: 34,
    paddingHorizontal: spacing[12],
    borderRadius: 999,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.10)",
    justifyContent: "center"
  },
  filterChipActive: {
    backgroundColor: palette.primary,
    borderColor: palette.primary
  },
  filterChipLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  },
  filterChipLabelActive: {
    color: palette.textPrimary
  },
  sectionWrap: {
    gap: spacing[12]
  },
  sectionHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  sectionCaption: {
    color: palette.textSecondary,
    ...typography.caption
  },
  featureRail: {
    gap: spacing[12],
    paddingRight: spacing[16]
  },
  featureCard: {
    width: 280,
    minHeight: 188,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    padding: spacing[16],
    gap: spacing[8]
  },
  featureTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  featureMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  featureBody: {
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
    fontWeight: "700"
  },
  stackList: {
    gap: spacing[12]
  },
  rowCard: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12],
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    padding: spacing[16]
  },
  rowCardCopy: {
    flex: 1,
    gap: 4
  },
  rowCardTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  rowCardMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  libraryCard: {
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.08)",
    backgroundColor: "rgba(255,255,255,0.04)",
    padding: spacing[16],
    gap: spacing[8]
  },
  libraryCardHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  libraryCardCopy: {
    flex: 1,
    gap: 4
  },
  libraryCardTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  libraryCardMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  libraryCardFreshness: {
    color: palette.textSecondary,
    ...typography.caption
  },
  libraryCardBody: {
    color: palette.textSecondary,
    ...typography.body2
  }
});




