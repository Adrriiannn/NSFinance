import { useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { DiscoverFilters } from "../../../src/components/planningHub/discover/DiscoverFilters";
import { DiscoverHeroCard } from "../../../src/components/planningHub/discover/DiscoverHeroCard";
import { FeaturedPlanCarousel } from "../../../src/components/planningHub/discover/FeaturedPlanCarousel";
import { PlanListCard } from "../../../src/components/planningHub/discover/PlanListCard";
import { PlanningHubShell } from "../../../src/components/planningHub/PlanningHubShell";
import {
  PLANNING_HUB_CONTENT_PADDING_X,
  PLANNING_HUB_CONTENT_TOP_GAP,
  getPlanningHubContentBottomInset
} from "../../../src/components/planningHub/planningHubLayout";
import { useExpensePlanning } from "../../../src/features/expenseTracker/ExpensePlanningProvider";
import { searchAndSortExpensePlanPublications } from "../../../src/features/expenseTracker/expensePlanCommunityUtils";
import type { ExpensePlanPeriodType, ExpensePlanPublication } from "../../../src/features/expenseTracker/expensePlanningTypes";
import { HeaderSearchSlot, HeaderShell } from "../../../src/layout/appHeader";
import { palette, spacing, typography } from "../../../src/theme/tokens";

type DiscoverRankingSort = "trending" | "most_liked" | "most_downloaded" | "recently_added";
type DiscoverPlanTypeFilter = "all" | ExpensePlanPeriodType | "templates";

const rankingOptions: Array<{ id: DiscoverRankingSort; label: string }> = [
  { id: "trending", label: "Trending" },
  { id: "most_liked", label: "Most liked" },
  { id: "most_downloaded", label: "Most downloaded" },
  { id: "recently_added", label: "Recently added" }
];

const planTypeOptions: Array<{ id: DiscoverPlanTypeFilter; label: string }> = [
  { id: "all", label: "All" },
  { id: "weekly", label: "Weekly" },
  { id: "monthly", label: "Monthly" },
  { id: "custom", label: "Custom" },
  { id: "templates", label: "Templates" }
];

function buildFilteredPublications(input: {
  publications: ExpensePlanPublication[];
  search: string;
  sort: DiscoverRankingSort;
  planTypeFilter: DiscoverPlanTypeFilter;
}) {
  const templatesOnly = input.planTypeFilter === "templates";
  const planType = input.planTypeFilter === "templates" ? "all" : input.planTypeFilter;

  return searchAndSortExpensePlanPublications({
    publications: input.publications,
    search: input.search,
    sort: input.sort,
    planType,
    creatorFilter: "",
    templatesOnly
  });
}

export default function PlanningHubDiscoverScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { publications } = useExpensePlanning();
  const [search, setSearch] = useState("");
  const [rankingSort, setRankingSort] = useState<DiscoverRankingSort>("trending");
  const [planTypeFilter, setPlanTypeFilter] = useState<DiscoverPlanTypeFilter>("all");

  const publishedPublications = useMemo(
    () => publications.filter((item) => item.publicationStatus === "published"),
    [publications]
  );

  const sortedSets = useMemo(
    () => ({
      trending: buildFilteredPublications({
        publications: publishedPublications,
        search,
        sort: "trending",
        planTypeFilter
      }),
      most_liked: buildFilteredPublications({
        publications: publishedPublications,
        search,
        sort: "most_liked",
        planTypeFilter
      }),
      most_downloaded: buildFilteredPublications({
        publications: publishedPublications,
        search,
        sort: "most_downloaded",
        planTypeFilter
      }),
      recently_added: buildFilteredPublications({
        publications: publishedPublications,
        search,
        sort: "recently_added",
        planTypeFilter
      })
    }),
    [planTypeFilter, publishedPublications, search]
  );

  const allPublicPlans = sortedSets[rankingSort];
  const featuredPlans = sortedSets.trending.slice(0, 6);
  const popularThisWeek = sortedSets.most_liked.slice(0, 6);
  const recentlyAdded = sortedSets.recently_added.slice(0, 6);

  const isVeryLimited = allPublicPlans.length <= 3;
  const isLimited = !isVeryLimited && allPublicPlans.length <= 6;

  return (
    <PlanningHubShell>
      <View style={styles.screen}>
        <HeaderShell
          preset="primaryDefault"
          includeTopInset
          bleedHorizontal={PLANNING_HUB_CONTENT_PADDING_X}
          title="Discover"
        />

        <ScrollView
          contentContainerStyle={[
            styles.scrollContent,
            {
              paddingTop: PLANNING_HUB_CONTENT_TOP_GAP,
              paddingBottom: getPlanningHubContentBottomInset(insets.bottom)
            }
          ]}
          showsVerticalScrollIndicator={false}
        >
          <DiscoverHeroCard
            livePlansCount={publishedPublications.length}
            onPressPublishPlan={() => router.push("/(tabs)/planning/publish" as never)}
            onPressMyPublishedPlans={() => router.push("/(tabs)/planning/my-published" as never)}
          />

          <HeaderSearchSlot
            value={search}
            onChangeText={setSearch}
            onClear={() => setSearch("")}
            placeholder="Search plans, tags, or creators"
            containerStyle={styles.searchSlot}
          />

          <View style={styles.filterSection}>
            <DiscoverFilters
              options={rankingOptions}
              selectedId={rankingSort}
              onSelect={(id) => setRankingSort(id as DiscoverRankingSort)}
              emphasis="primary"
            />
            <DiscoverFilters
              options={planTypeOptions}
              selectedId={planTypeFilter}
              onSelect={(id) => setPlanTypeFilter(id as DiscoverPlanTypeFilter)}
              emphasis="secondary"
            />
          </View>

          {allPublicPlans.length === 0 ? (
            <EmptyState
              title="No public plans found"
              message="Try another search term or filter combination."
            />
          ) : (
            <>
              {!isLimited && featuredPlans.length > 0 ? (
                <View style={styles.section}>
                  <Text style={styles.sectionTitle}>Featured</Text>
                  <FeaturedPlanCarousel
                    items={featuredPlans}
                    onPressPublication={(publicationId) =>
                      router.push(`/(tabs)/planning/published/${publicationId}` as never)
                    }
                  />
                </View>
              ) : null}

              {isVeryLimited ? (
                recentlyAdded.length > 0 ? (
                  <View style={styles.section}>
                    <Text style={styles.sectionTitle}>Recently added</Text>
                    <View style={styles.listWrap}>
                      {recentlyAdded.map((publication) => (
                        <PlanListCard
                          key={publication.id}
                          publication={publication}
                          onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}
                          variant="compact"
                          showDate
                        />
                      ))}
                    </View>
                  </View>
                ) : null
              ) : (
                popularThisWeek.length > 0 ? (
                  <View style={styles.section}>
                    <Text style={styles.sectionTitle}>Popular this week</Text>
                    <View style={styles.listWrap}>
                      {popularThisWeek.map((publication) => (
                        <PlanListCard
                          key={publication.id}
                          publication={publication}
                          onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}
                          variant="compact"
                        />
                      ))}
                    </View>
                  </View>
                ) : null
              )}

              {!isLimited && recentlyAdded.length > 0 ? (
                <View style={styles.section}>
                  <Text style={styles.sectionTitle}>Recently added</Text>
                  <View style={styles.listWrap}>
                    {recentlyAdded.map((publication) => (
                      <PlanListCard
                        key={publication.id}
                        publication={publication}
                        onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}
                        variant="compact"
                        showDate
                      />
                    ))}
                  </View>
                </View>
              ) : null}

              <View style={styles.section}>
                <View style={styles.sectionHeaderRow}>
                  <Text style={styles.sectionTitle}>All public plans</Text>
                  <Text style={styles.sectionCount}>{allPublicPlans.length}</Text>
                </View>
                <View style={styles.listWrap}>
                  {allPublicPlans.map((publication) => (
                    <PlanListCard
                      key={publication.id}
                      publication={publication}
                      onPress={() => router.push(`/(tabs)/planning/published/${publication.id}` as never)}
                      variant="full"
                      showDate={rankingSort === "recently_added"}
                    />
                  ))}
                </View>
              </View>
            </>
          )}
        </ScrollView>
      </View>
    </PlanningHubShell>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1
  },
  scrollContent: {
    gap: spacing[16]
  },
  searchSlot: {
    width: "100%"
  },
  filterSection: {
    gap: spacing[10]
  },
  section: {
    gap: spacing[12]
  },
  sectionHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[8]
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "800"
  },
  sectionCount: {
    color: palette.textSecondary,
    ...typography.caption
  },
  listWrap: {
    gap: spacing[10]
  }
});

