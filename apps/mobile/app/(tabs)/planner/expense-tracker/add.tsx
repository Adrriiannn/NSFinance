import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { LayoutAnimation, Platform, Pressable, StyleSheet, Text, UIManager, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { ExpenseTrackerMiniAppScreen } from "../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../../src/components/ui/PrimaryButton";
import { TextField } from "../../../../src/components/ui/TextField";
import { useExpensePlanning } from "../../../../src/features/expenseTracker/ExpensePlanningProvider";
import {
  flattenVisibleExpenseTaxonomy,
  getExpenseTrackerSubcategoryVisual,
  getExpenseTrackerVisual
} from "../../../../src/features/expenseTracker/expenseTrackerModels";
import {
  buildExpenseTaxonomySearchIndex,
  normalizeExpenseTaxonomySearchText,
  searchExpenseTaxonomy
} from "../../../../src/features/expenseTracker/expenseTaxonomySearch";
import { useExpenseTrackerTaxonomyQuery } from "../../../../src/features/expenseTracker/useExpenseTracker";
import { getFloatingTabBarInset } from "../../../../src/theme/insets";
import { palette, radius, spacing, typography } from "../../../../src/theme/tokens";

export default function ExpenseTrackerAddScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const params = useLocalSearchParams<{ selectionMode?: string; lineItemId?: string }>();
  const selectionMode = params.selectionMode === "true";
  const lineItemId = typeof params.lineItemId === "string" ? params.lineItemId : "";
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const { assignBuilderLineItemSubcategory, setSelectionLineItemId } = useExpensePlanning();
  const [searchQuery, setSearchQuery] = useState("");
  const [expandedDomainId, setExpandedDomainId] = useState<number | null>(null);
  const [expandedCategoryIds, setExpandedCategoryIds] = useState<Record<number, number | null>>({});
  const [pendingSubcategoryId, setPendingSubcategoryId] = useState<number | null>(null);

  const visibleDomains = useMemo(
    () => (taxonomyQuery.data?.domains ?? []).filter((domain) => domain.isUserSelectable && !domain.isSystemDomain && domain.isActive),
    [taxonomyQuery.data?.domains]
  );
  const flattenedSelections = useMemo(() => flattenVisibleExpenseTaxonomy(visibleDomains), [visibleDomains]);
  const selectionBySubcategoryId = useMemo(
    () => new Map(flattenedSelections.map((item) => [item.subcategory.id, item] as const)),
    [flattenedSelections]
  );
  const searchIndex = useMemo(() => buildExpenseTaxonomySearchIndex(visibleDomains), [visibleDomains]);
  const searchResults = useMemo(() => searchExpenseTaxonomy(searchIndex, searchQuery), [searchIndex, searchQuery]);
  const hasSearchQuery = normalizeExpenseTaxonomySearchText(searchQuery).length > 0;

  useEffect(() => {
    if (Platform.OS === "android" && UIManager.setLayoutAnimationEnabledExperimental) {
      UIManager.setLayoutAnimationEnabledExperimental(true);
    }
  }, []);

  useEffect(() => {
    if (!selectionMode || !lineItemId) {
      return;
    }

    setSelectionLineItemId(lineItemId);
  }, [lineItemId, selectionMode, setSelectionLineItemId]);

  const handleSubcategoryPress = (subcategoryId: number) => {
    setPendingSubcategoryId(subcategoryId);

    if (!selectionMode) {
      return;
    }

    const selection = selectionBySubcategoryId.get(subcategoryId);
    if (!selection) {
      return;
    }

    setExpandedDomainId(selection.domain.id);
    setExpandedCategoryIds((current) => ({
      ...current,
      [selection.domain.id]: selection.category.id
    }));
  };

  const confirmSelection = () => {
    if (!selectionMode || !lineItemId || !pendingSubcategoryId) {
      return;
    }

    assignBuilderLineItemSubcategory(lineItemId, pendingSubcategoryId);
    router.back();
  };

  return (
    <View style={styles.screenWrap}>
      <ExpenseTrackerMiniAppScreen title={selectionMode ? "Select category" : "Categories"}>
        {taxonomyQuery.isError ? (
          <ErrorState
            title="Could not load categories"
            message={taxonomyQuery.error.message}
            onRetry={() => {
              void taxonomyQuery.refetch();
            }}
          />
        ) : null}

        <View style={styles.categoryPickerSection}>
          <View style={styles.categorySearchWrap}>
            <TextField
              label="Search categories"
              showLabel={false}
              value={searchQuery}
              onChangeText={setSearchQuery}
              placeholder="Search categories, keywords, or brands"
            />
          </View>

          <GlassCard style={styles.categoryLauncherCard}>
            {hasSearchQuery ? (
              searchResults.length > 0 ? (
                <View style={styles.searchResultsList}>
                  {searchResults.slice(0, 20).map((result) => {
                    const selected = selectionMode && pendingSubcategoryId === result.item.subcategoryId;
                    const visual = getExpenseTrackerSubcategoryVisual({
                      domainId: result.item.domainId,
                      categoryId: result.item.categoryId,
                      subcategoryId: result.item.subcategoryId,
                      subcategoryName: result.item.subcategoryName
                    });

                    return (
                      <Pressable
                        key={result.item.subcategoryId}
                        style={({ pressed }) => [
                          styles.searchResultRow,
                          selectionMode && pressed ? styles.searchResultRowPressed : null,
                          selected ? styles.searchResultRowSelected : null,
                          {
                            borderColor: selected ? visual.color : palette.border,
                            backgroundColor: selected ? `${visual.color}18` : "rgba(18,36,58,0.82)"
                          }
                        ]}
                        onPress={selectionMode ? () => handleSubcategoryPress(result.item.subcategoryId) : undefined}
                      >
                        <View style={[styles.searchResultIconWrap, { backgroundColor: `${visual.color}20` }]}>
                          <Ionicons name={visual.icon as keyof typeof Ionicons.glyphMap} size={16} color={visual.color} />
                        </View>
                        <View style={styles.searchResultCopy}>
                          <Text style={styles.searchResultTitle}>{result.item.subcategoryName}</Text>
                          <Text style={styles.searchResultPath}>{result.item.categoryName} • {result.item.domainName}</Text>
                        </View>
                        {selectionMode && selected ? <Ionicons name="checkmark" size={18} color={visual.color} /> : null}
                      </Pressable>
                    );
                  })}
                </View>
              ) : (
                <View style={styles.searchEmptyState}>
                  <Text style={styles.searchEmptyTitle}>No category found</Text>
                  <Text style={styles.searchEmptyText}>Try another word or clear search and browse manually.</Text>
                </View>
              )
            ) : (
              <View style={styles.domainList}>
                {visibleDomains.map((domain) => {
                  const domainVisuals = getExpenseTrackerVisual({ domainId: domain.id });
                  const isDomainExpanded = expandedDomainId === domain.id;
                  const expandedCategoryId = expandedCategoryIds[domain.id] ?? null;

                  return (
                    <View key={domain.id} style={styles.domainSection}>
                      <Pressable
                        onPress={() => {
                          LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
                          setExpandedDomainId((current) => (current === domain.id ? null : domain.id));
                        }}
                        style={({ pressed }) => [styles.domainButton, pressed ? styles.domainButtonPressed : null]}
                      >
                        <View style={styles.domainButtonLeft}>
                          <View style={[styles.domainIconWrap, { backgroundColor: `${domainVisuals.color}18` }]}>
                            <Ionicons name={domainVisuals.icon as keyof typeof Ionicons.glyphMap} size={16} color={domainVisuals.color} />
                          </View>
                          <Text style={styles.domainTitle}>{domain.name}</Text>
                        </View>
                        <Ionicons name={isDomainExpanded ? "chevron-up" : "chevron-down"} size={18} color={palette.textSecondary} />
                      </Pressable>

                      {isDomainExpanded ? (
                        <>
                          <View style={styles.domainCategoryDividerWrap}>
                            <View style={styles.domainCategoryDivider} />
                          </View>

                          <View style={styles.categorySectionList}>
                            {domain.categories.filter((category) => category.isUserSelectable && category.isActive).map((category) => {
                              const categoryVisuals = getExpenseTrackerVisual({ domainId: domain.id, categoryId: category.id });
                              const isCategoryExpanded = expandedCategoryId === category.id;

                              return (
                                <View key={category.id} style={styles.categorySection}>
                                  <Pressable
                                    onPress={() => {
                                      LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
                                      setExpandedCategoryIds((current) => ({
                                        ...current,
                                        [domain.id]: current[domain.id] === category.id ? null : category.id
                                      }));
                                    }}
                                    style={({ pressed }) => [styles.categoryButton, pressed ? styles.categoryButtonPressed : null]}
                                  >
                                    <View style={styles.categoryButtonLeft}>
                                      <View style={[styles.categoryAccordionIconWrap, { backgroundColor: `${categoryVisuals.color}18` }]}>
                                        <Ionicons name={categoryVisuals.icon as keyof typeof Ionicons.glyphMap} size={15} color={categoryVisuals.color} />
                                      </View>
                                      <Text style={styles.categoryHeading}>{category.name}</Text>
                                    </View>
                                    <Ionicons name={isCategoryExpanded ? "chevron-up" : "chevron-down"} size={17} color={palette.textSecondary} />
                                  </Pressable>

                                  {isCategoryExpanded ? (
                                    <View style={styles.subcategoryList}>
                                      {category.subcategories.filter((subcategory) => subcategory.isUserSelectable && subcategory.isActive).map((subcategory) => {
                                        const selected = selectionMode && pendingSubcategoryId === subcategory.id;
                                        const subcategoryVisuals = getExpenseTrackerSubcategoryVisual({
                                          domainId: domain.id,
                                          categoryId: category.id,
                                          subcategoryId: subcategory.id,
                                          subcategoryName: subcategory.name
                                        });

                                        return (
                                          <Pressable
                                            key={subcategory.id}
                                            style={({ pressed }) => [
                                              styles.subcategoryRow,
                                              selectionMode && pressed ? styles.subcategoryRowPressed : null,
                                              selected ? styles.subcategoryRowSelected : null,
                                              {
                                                borderColor: selected ? subcategoryVisuals.color : palette.border,
                                                backgroundColor: selected ? `${subcategoryVisuals.color}18` : "rgba(18,36,58,0.82)"
                                              }
                                            ]}
                                            onPress={selectionMode ? () => handleSubcategoryPress(subcategory.id) : undefined}
                                          >
                                            <View style={[styles.subcategoryIconWrap, { backgroundColor: `${subcategoryVisuals.color}20` }]}>
                                              <Ionicons
                                                name={subcategoryVisuals.icon as keyof typeof Ionicons.glyphMap}
                                                size={16}
                                                color={subcategoryVisuals.color}
                                              />
                                            </View>
                                            <Text style={styles.subcategoryLabel}>{subcategory.name}</Text>
                                            {selectionMode && selected ? <Ionicons name="checkmark" size={18} color={subcategoryVisuals.color} /> : null}
                                          </Pressable>
                                        );
                                      })}
                                    </View>
                                  ) : null}
                                </View>
                              );
                            })}
                          </View>
                        </>
                      ) : null}
                    </View>
                  );
                })}
              </View>
            )}
          </GlassCard>
        </View>
      </ExpenseTrackerMiniAppScreen>

      {selectionMode ? (
        <View pointerEvents="box-none" style={StyleSheet.absoluteFill}>
          <View style={[styles.confirmBar, { bottom: getFloatingTabBarInset(insets.bottom, 4) }]}>
            <PrimaryButton label="Confirm selection" onPress={confirmSelection} disabled={!pendingSubcategoryId} />
          </View>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  screenWrap: {
    flex: 1
  },
  categoryPickerSection: {
    gap: spacing[8]
  },
  categorySearchWrap: {
    marginBottom: 0
  },
  categoryLauncherCard: {
    gap: spacing[16],
    marginTop: 0
  },
  searchResultsList: {
    gap: spacing[8]
  },
  searchResultRow: {
    minHeight: 58,
    borderRadius: 18,
    borderWidth: 1,
    paddingHorizontal: 12,
    paddingVertical: 10,
    flexDirection: "row",
    alignItems: "center",
    gap: 10
  },
  searchResultRowPressed: {
    opacity: 0.96
  },
  searchResultRowSelected: {
    borderWidth: 1.2
  },
  searchResultIconWrap: {
    width: 32,
    height: 32,
    borderRadius: 11,
    alignItems: "center",
    justifyContent: "center"
  },
  searchResultCopy: {
    flex: 1,
    gap: spacing[8]
  },
  searchResultTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  searchResultPath: {
    color: palette.textSecondary,
    ...typography.caption
  },
  searchEmptyState: {
    minHeight: 112,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.58)",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[20],
    gap: spacing[8]
  },
  searchEmptyTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  searchEmptyText: {
    color: palette.textSecondary,
    ...typography.body2,
    textAlign: "center"
  },
  domainList: {
    gap: 10
  },
  domainSection: {
    gap: 10
  },
  domainButton: {
    minHeight: 40,
    paddingVertical: spacing[8],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  domainButtonPressed: {
    opacity: 0.82
  },
  domainButtonLeft: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    gap: 10
  },
  domainIconWrap: {
    width: 30,
    height: 30,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center"
  },
  domainTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  domainCategoryDividerWrap: {
    alignItems: "center",
    paddingTop: 2,
    paddingBottom: spacing[4]
  },
  domainCategoryDivider: {
    width: "70%",
    height: 1,
    borderRadius: 999,
    backgroundColor: "rgba(213, 229, 255, 0.08)"
  },
  categorySectionList: {
    gap: 10,
    paddingLeft: spacing[8]
  },
  categorySection: {
    gap: 8
  },
  categoryButton: {
    minHeight: 38,
    paddingVertical: spacing[4],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  categoryButtonPressed: {
    opacity: 0.82
  },
  categoryButtonLeft: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    gap: 10
  },
  categoryAccordionIconWrap: {
    width: 28,
    height: 28,
    borderRadius: 10,
    alignItems: "center",
    justifyContent: "center"
  },
  categoryHeading: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "700"
  },
  subcategoryList: {
    gap: spacing[8],
    paddingLeft: 10,
    paddingTop: 2
  },
  subcategoryRow: {
    minHeight: 50,
    borderRadius: 16,
    borderWidth: 1,
    paddingHorizontal: 12,
    paddingVertical: 10,
    flexDirection: "row",
    alignItems: "center",
    gap: 10
  },
  subcategoryRowPressed: {
    opacity: 0.96
  },
  subcategoryRowSelected: {
    borderWidth: 1.2
  },
  subcategoryIconWrap: {
    width: 30,
    height: 30,
    borderRadius: 10,
    alignItems: "center",
    justifyContent: "center"
  },
  subcategoryLabel: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "700"
  },
  confirmBar: {
    position: "absolute",
    left: spacing[20],
    right: spacing[20]
  }
});
