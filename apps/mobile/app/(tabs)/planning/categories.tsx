import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, Easing, LayoutAnimation, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { PlanningHubShell } from "../../../src/components/planningHub/PlanningHubShell";
import { PlanningHubScreen } from "../../../src/components/planningHub/PlanningHubScreen";
import {
  PLANNING_HUB_CONTENT_PADDING_X,
  PLANNING_HUB_CONTENT_TOP_GAP,
  getPlanningHubContentBottomInset
} from "../../../src/components/planningHub/planningHubLayout";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import { useExpensePlanning } from "../../../src/features/expenseTracker/ExpensePlanningProvider";
import {
  flattenVisibleExpenseTaxonomy,
  getExpenseTrackerSubcategoryVisual,
  getExpenseTrackerVisual
} from "../../../src/features/expenseTracker/expenseTrackerModels";
import {
  buildExpenseTaxonomySearchIndex,
  normalizeExpenseTaxonomySearchText,
  searchExpenseTaxonomy
} from "../../../src/features/expenseTracker/expenseTaxonomySearch";
import {
  setPendingTransactionDetailCategorySelection,
  setPendingActivityAddTransactionSubcategorySelection,
  setPendingActivitySearchCategorySelection,
  type ActivitySearchCategorySelection,
  type TransactionDetailCategorySelection
} from "../../../src/features/expenseTracker/categoryPickerBridge";
import { useExpenseTrackerTaxonomyQuery } from "../../../src/features/expenseTracker/useExpenseTracker";
import { TRANSFER_DOMAIN_ID } from "../../../src/features/transactions/transferClassification";
import { HeaderSearchSlot, HeaderShell } from "../../../src/layout/appHeader";
import { getFloatingTabBarInset } from "../../../src/theme/insets";
import { palette, radius, spacing, surfaces, typography, createRuntimeStyleSheet, useThemeTokens } from "../../../src/theme/tokens";
import type { ExpenseTaxonomyDomainDto } from "../../../src/types/api";

function normalizeDomainForCategoriesPage(domain: ExpenseTaxonomyDomainDto): ExpenseTaxonomyDomainDto {
  if (domain.id !== TRANSFER_DOMAIN_ID) {
    return domain;
  }

  return {
    ...domain,
    isUserSelectable: true,
    isSystemDomain: false,
    categories: domain.categories
      .filter((category) => category.isActive)
      .map((category) => ({
        ...category,
        isUserSelectable: true,
        subcategories: category.subcategories
          .filter((subcategory) => subcategory.isActive)
          .map((subcategory) => ({
            ...subcategory,
            isUserSelectable: true
          }))
      }))
  };
}

export default function PlanningHubCategoriesScreen() {
  useThemeTokens();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const params = useLocalSearchParams<{
    selectionMode?: string;
    lineItemId?: string;
    selectionTarget?: string;
  }>();
  const selectionMode = params.selectionMode === "true";
  const lineItemId = typeof params.lineItemId === "string" ? params.lineItemId : "";
  const selectionTarget =
    typeof params.selectionTarget === "string" ? params.selectionTarget : "";
  const selectionReturnPath: "/(tabs)/activity" | "/(tabs)/activity/add" | null =
    selectionTarget === "activitySearchCategoryFilter"
      ? "/(tabs)/activity"
      : selectionTarget === "activityAddTransaction"
        ? "/(tabs)/activity/add"
        : null;
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const { assignBuilderLineItemSubcategory, setSelectionLineItemId } = useExpensePlanning();
  const [searchQuery, setSearchQuery] = useState("");
  const [debouncedSearchQuery, setDebouncedSearchQuery] = useState("");
  const [expandedDomainId, setExpandedDomainId] = useState<number | null>(null);
  const [expandedCategoryIds, setExpandedCategoryIds] = useState<Record<number, number | null>>({});
  const [pendingSubcategoryId, setPendingSubcategoryId] = useState<number | null>(null);
  const [pendingTransactionDetailSelection, setPendingTransactionDetailSelection] =
    useState<TransactionDetailCategorySelection | null>(null);
  const [pendingHierarchySelection, setPendingHierarchySelection] =
    useState<ActivitySearchCategorySelection | null>(null);
  const isActivitySearchCategorySelection =
    selectionMode && selectionTarget === "activitySearchCategoryFilter";
  const isTransactionDetailCategorySelection =
    selectionMode && selectionTarget === "transactionDetailCategory";

  const returnToSelectionOrigin = () => {
    if (selectionReturnPath) {
      router.replace(selectionReturnPath);
      return;
    }

    router.back();
  };

  const visibleDomains = useMemo(
    () =>
      (taxonomyQuery.data?.domains ?? [])
        .filter(
          (domain) =>
            domain.isActive
            && ((!domain.isSystemDomain && domain.isUserSelectable) || domain.id === TRANSFER_DOMAIN_ID)
        )
        .map(normalizeDomainForCategoriesPage),
    [taxonomyQuery.data?.domains]
  );
  const flattenedSelections = useMemo(() => flattenVisibleExpenseTaxonomy(visibleDomains), [visibleDomains]);
  const selectionBySubcategoryId = useMemo(
    () => new Map(flattenedSelections.map((item) => [item.subcategory.id, item] as const)),
    [flattenedSelections]
  );
  const subcategoryIdsByCategoryId = useMemo(() => {
    const map = new Map<number, Set<number>>();
    flattenedSelections.forEach((item) => {
      const current = map.get(item.category.id);
      if (current) {
        current.add(item.subcategory.id);
      } else {
        map.set(item.category.id, new Set([item.subcategory.id]));
      }
    });
    return map;
  }, [flattenedSelections]);
  const searchIndex = useMemo(() => buildExpenseTaxonomySearchIndex(visibleDomains), [visibleDomains]);
  const searchResults = useMemo(
    () => searchExpenseTaxonomy(searchIndex, debouncedSearchQuery),
    [debouncedSearchQuery, searchIndex]
  );
  const hasSearchQuery = normalizeExpenseTaxonomySearchText(debouncedSearchQuery).length > 0;

  useEffect(() => {
    const handle = setTimeout(() => {
      setDebouncedSearchQuery(searchQuery);
    }, 150);

    return () => {
      clearTimeout(handle);
    };
  }, [searchQuery]);

  useEffect(() => {
    if (!selectionMode || !lineItemId) {
      return;
    }

    setSelectionLineItemId(lineItemId);
  }, [lineItemId, selectionMode, setSelectionLineItemId]);

  const buildDomainSelection = (domainId: number, domainName: string): ActivitySearchCategorySelection => ({
    scope: "domain",
    domainId,
    domainName,
    categoryId: null,
    categoryName: "",
    subcategoryId: null,
    subcategoryName: "",
    excludedCategoryIds: [],
    excludedSubcategoryIds: []
  });

  const buildCategorySelection = (
    domainId: number,
    domainName: string,
    categoryId: number,
    categoryName: string
  ): ActivitySearchCategorySelection => ({
    scope: "category",
    domainId,
    domainName,
    categoryId,
    categoryName,
    subcategoryId: null,
    subcategoryName: "",
    excludedCategoryIds: [],
    excludedSubcategoryIds: []
  });

  const buildSubcategorySelection = (
    domainId: number,
    domainName: string,
    categoryId: number,
    categoryName: string,
    subcategoryId: number,
    subcategoryName: string
  ): ActivitySearchCategorySelection => ({
    scope: "subcategory",
    domainId,
    domainName,
    categoryId,
    categoryName,
    subcategoryId,
    subcategoryName,
    excludedCategoryIds: [],
    excludedSubcategoryIds: []
  });

  const buildTransactionDetailCategorySelection = (
    domainId: number,
    domainName: string,
    categoryId: number,
    categoryName: string
  ): TransactionDetailCategorySelection => ({
    domainId,
    domainName,
    categoryId,
    categoryName,
    subcategoryId: null,
    subcategoryName: ""
  });

  const buildTransactionDetailSubcategorySelection = (
    domainId: number,
    domainName: string,
    categoryId: number,
    categoryName: string,
    subcategoryId: number,
    subcategoryName: string
  ): TransactionDetailCategorySelection => ({
    domainId,
    domainName,
    categoryId,
    categoryName,
    subcategoryId,
    subcategoryName
  });

  const stripCategorySubcategoryExclusions = (
    excludedSubcategoryIds: number[],
    categoryId: number
  ) => {
    const subcategoryIds = subcategoryIdsByCategoryId.get(categoryId);
    if (!subcategoryIds) {
      return excludedSubcategoryIds;
    }

    return excludedSubcategoryIds.filter((id) => !subcategoryIds.has(id));
  };

  const handleSubcategoryPress = (subcategoryId: number) => {
    if (isActivitySearchCategorySelection) {
      const selection = selectionBySubcategoryId.get(subcategoryId);
      if (selection) {
        setPendingHierarchySelection((current) => {
          if (!current) {
            return buildSubcategorySelection(
              selection.domain.id,
              selection.domain.name,
              selection.category.id,
              selection.category.name,
              selection.subcategory.id,
              selection.subcategory.name
            );
          }

          if (current.scope === "domain" && current.domainId === selection.domain.id) {
            if (current.excludedCategoryIds.includes(selection.category.id)) {
              return current;
            }

            const isExcluded = current.excludedSubcategoryIds.includes(selection.subcategory.id);
            return {
              ...current,
              excludedSubcategoryIds: isExcluded
                ? current.excludedSubcategoryIds.filter((id) => id !== selection.subcategory.id)
                : [...current.excludedSubcategoryIds, selection.subcategory.id]
            };
          }

          if (current.scope === "category" && current.categoryId === selection.category.id) {
            const isExcluded = current.excludedSubcategoryIds.includes(selection.subcategory.id);
            return {
              ...current,
              excludedSubcategoryIds: isExcluded
                ? current.excludedSubcategoryIds.filter((id) => id !== selection.subcategory.id)
                : [...current.excludedSubcategoryIds, selection.subcategory.id]
            };
          }

          if (
            current.scope === "subcategory" &&
            current.subcategoryId === selection.subcategory.id
          ) {
            return null;
          }

          return buildSubcategorySelection(
            selection.domain.id,
            selection.domain.name,
            selection.category.id,
            selection.category.name,
            selection.subcategory.id,
            selection.subcategory.name
          );
        });
      }
    } else if (isTransactionDetailCategorySelection) {
      const selection = selectionBySubcategoryId.get(subcategoryId);
      if (selection) {
        setPendingTransactionDetailSelection((current) => {
          if (current?.subcategoryId === subcategoryId) {
            return null;
          }

          return buildTransactionDetailSubcategorySelection(
            selection.domain.id,
            selection.domain.name,
            selection.category.id,
            selection.category.name,
            selection.subcategory.id,
            selection.subcategory.name
          );
        });
      }
    } else {
      setPendingSubcategoryId(subcategoryId);
    }

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

  const handleDomainSelectionToggle = (domainId: number, domainName: string) => {
    setPendingHierarchySelection((current) => {
      if (current?.scope === "domain" && current.domainId === domainId) {
        return null;
      }

      return buildDomainSelection(domainId, domainName);
    });
  };

  const handleCategorySelectionToggle = (
    domainId: number,
    domainName: string,
    categoryId: number,
    categoryName: string
  ) => {
    if (isTransactionDetailCategorySelection) {
      setPendingTransactionDetailSelection((current) => {
        if (
          current?.categoryId === categoryId
          && current.subcategoryId === null
        ) {
          return null;
        }

        return buildTransactionDetailCategorySelection(domainId, domainName, categoryId, categoryName);
      });
      return;
    }

    setPendingHierarchySelection((current) => {
      if (current?.scope === "domain" && current.domainId === domainId) {
        const isExcluded = current.excludedCategoryIds.includes(categoryId);
        if (isExcluded) {
          return {
            ...current,
            excludedCategoryIds: current.excludedCategoryIds.filter((id) => id !== categoryId)
          };
        }

        return {
          ...current,
          excludedCategoryIds: [...current.excludedCategoryIds, categoryId],
          excludedSubcategoryIds: stripCategorySubcategoryExclusions(
            current.excludedSubcategoryIds,
            categoryId
          )
        };
      }

      if (current?.scope === "category" && current.categoryId === categoryId) {
        return null;
      }

      return buildCategorySelection(domainId, domainName, categoryId, categoryName);
    });
  };

  const isDomainChecked = (domainId: number) =>
    pendingHierarchySelection?.scope === "domain" &&
    pendingHierarchySelection.domainId === domainId;

  const isCategoryChecked = (domainId: number, categoryId: number) => {
    if (!pendingHierarchySelection) {
      return false;
    }

    if (
      pendingHierarchySelection.scope === "domain" &&
      pendingHierarchySelection.domainId === domainId
    ) {
      return !pendingHierarchySelection.excludedCategoryIds.includes(categoryId);
    }

    return (
      pendingHierarchySelection.scope === "category" &&
      pendingHierarchySelection.categoryId === categoryId
    );
  };

  const isSubcategoryChecked = (
    domainId: number,
    categoryId: number,
    subcategoryId: number
  ) => {
    if (isTransactionDetailCategorySelection) {
      return pendingTransactionDetailSelection?.subcategoryId === subcategoryId;
    }

    if (!pendingHierarchySelection) {
      return false;
    }

    if (
      pendingHierarchySelection.scope === "domain" &&
      pendingHierarchySelection.domainId === domainId
    ) {
      if (pendingHierarchySelection.excludedCategoryIds.includes(categoryId)) {
        return false;
      }

      return !pendingHierarchySelection.excludedSubcategoryIds.includes(subcategoryId);
    }

    if (
      pendingHierarchySelection.scope === "category" &&
      pendingHierarchySelection.categoryId === categoryId
    ) {
      return !pendingHierarchySelection.excludedSubcategoryIds.includes(subcategoryId);
    }

    return (
      pendingHierarchySelection.scope === "subcategory" &&
      pendingHierarchySelection.subcategoryId === subcategoryId
    );
  };

  const confirmSelection = () => {
    if (!selectionMode) {
      return;
    }

    if (selectionTarget === "activitySearchCategoryFilter") {
      if (!pendingHierarchySelection) {
        return;
      }

      setPendingActivitySearchCategorySelection(pendingHierarchySelection);
      returnToSelectionOrigin();
      return;
    }

    if (selectionTarget === "transactionDetailCategory") {
      if (!pendingTransactionDetailSelection) {
        return;
      }

      setPendingTransactionDetailCategorySelection(pendingTransactionDetailSelection);
      returnToSelectionOrigin();
      return;
    }

    if (!pendingSubcategoryId) {
      return;
    }

    if (selectionTarget === "activityAddTransaction") {
      setPendingActivityAddTransactionSubcategorySelection(pendingSubcategoryId);
      returnToSelectionOrigin();
      return;
    }

    if (!lineItemId) {
      return;
    }

    assignBuilderLineItemSubcategory(lineItemId, pendingSubcategoryId);
    router.back();
  };

  const hasPendingSelection = isActivitySearchCategorySelection
    ? Boolean(pendingHierarchySelection)
    : isTransactionDetailCategorySelection
      ? Boolean(pendingTransactionDetailSelection)
      : Boolean(pendingSubcategoryId);
  const confirmAnimation = useRef(new Animated.Value(hasPendingSelection ? 1 : 0)).current;
  const confirmVisibleBottom = getFloatingTabBarInset(insets.bottom, 20);
  const confirmHiddenTranslateY = confirmVisibleBottom + spacing[12];

  useEffect(() => {
    Animated.timing(confirmAnimation, {
      toValue: hasPendingSelection ? 1 : 0,
      duration: 220,
      easing: hasPendingSelection ? Easing.out(Easing.cubic) : Easing.in(Easing.cubic),
      useNativeDriver: true
    }).start();
  }, [confirmAnimation, hasPendingSelection]);

  const content = (
    <>
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
          {selectionMode ? (
            <View style={styles.categorySearchWrap}>
              <TextField
                label="Search categories"
                showLabel={false}
                value={searchQuery}
                onChangeText={setSearchQuery}
                placeholder="Search categories, keywords, or brands"
              />
            </View>
          ) : null}

          <GlassCard style={styles.categoryLauncherCard}>
            {hasSearchQuery ? (
              searchResults.length > 0 ? (
                <View style={styles.searchResultsList}>
                  {searchResults.slice(0, 20).map((result) => {
                    const selected = selectionMode && (
                      isActivitySearchCategorySelection
                        ? isSubcategoryChecked(
                            result.item.domainId,
                            result.item.categoryId,
                            result.item.subcategoryId
                          )
                        : isTransactionDetailCategorySelection
                          ? pendingTransactionDetailSelection?.subcategoryId === result.item.subcategoryId
                          : pendingSubcategoryId === result.item.subcategoryId
                    );
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
                            backgroundColor: selected ? `${visual.color}18` : surfaces.field
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
                        {selectionMode ? (
                          isActivitySearchCategorySelection || isTransactionDetailCategorySelection ? (
                            <View
                              style={[
                                styles.selectionCheckbox,
                                selected ? styles.selectionCheckboxChecked : null
                              ]}
                            >
                              {selected ? (
                                <Ionicons name="checkmark" size={13} color={palette.textPrimary} />
                              ) : null}
                            </View>
                          ) : selected ? (
                            <Ionicons name="checkmark" size={18} color={visual.color} />
                          ) : null
                        ) : null}
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
                        <View style={styles.rowActionRail}>
                          <Ionicons
                            name={isDomainExpanded ? "chevron-up" : "chevron-down"}
                            size={18}
                            color={palette.textSecondary}
                          />
                          {isActivitySearchCategorySelection ? (
                            <Pressable
                              onPress={() => handleDomainSelectionToggle(domain.id, domain.name)}
                              style={({ pressed }) => [
                                styles.selectionCheckbox,
                                isDomainChecked(domain.id) ? styles.selectionCheckboxChecked : null,
                                pressed ? styles.selectionCheckboxPressed : null
                              ]}
                            >
                              {isDomainChecked(domain.id) ? (
                                <Ionicons name="checkmark" size={13} color={palette.textPrimary} />
                              ) : null}
                            </Pressable>
                          ) : null}
                        </View>
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
                                    <View style={styles.rowActionRail}>
                                      <Ionicons
                                        name={isCategoryExpanded ? "chevron-up" : "chevron-down"}
                                        size={17}
                                        color={palette.textSecondary}
                                      />
                                      {isActivitySearchCategorySelection || isTransactionDetailCategorySelection ? (
                                        <Pressable
                                          onPress={() =>
                                            handleCategorySelectionToggle(
                                              domain.id,
                                              domain.name,
                                              category.id,
                                              category.name
                                            )
                                          }
                                          style={({ pressed }) => [
                                            styles.selectionCheckbox,
                                            (
                                              isTransactionDetailCategorySelection
                                                ? pendingTransactionDetailSelection?.categoryId === category.id
                                                : isCategoryChecked(domain.id, category.id)
                                            )
                                              ? styles.selectionCheckboxChecked
                                              : null,
                                            pressed ? styles.selectionCheckboxPressed : null
                                          ]}
                                        >
                                          {(isTransactionDetailCategorySelection
                                            ? pendingTransactionDetailSelection?.categoryId === category.id
                                            : isCategoryChecked(domain.id, category.id)) ? (
                                            <Ionicons
                                              name="checkmark"
                                              size={13}
                                              color={palette.textPrimary}
                                            />
                                          ) : null}
                                        </Pressable>
                                      ) : null}
                                    </View>
                                  </Pressable>

                                  {isCategoryExpanded ? (
                                    <View style={styles.subcategoryList}>
                                      {category.subcategories.filter((subcategory) => subcategory.isUserSelectable && subcategory.isActive).map((subcategory) => {
                                        const selected = selectionMode && (
                                          isActivitySearchCategorySelection
                                            ? isSubcategoryChecked(domain.id, category.id, subcategory.id)
                                            : isTransactionDetailCategorySelection
                                              ? pendingTransactionDetailSelection?.subcategoryId === subcategory.id
                                              : pendingSubcategoryId === subcategory.id
                                        );
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
                                                backgroundColor: selected ? `${subcategoryVisuals.color}18` : surfaces.field
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
                                            {selectionMode ? (
                                              isActivitySearchCategorySelection || isTransactionDetailCategorySelection ? (
                                                <View
                                                  style={[
                                                    styles.selectionCheckbox,
                                                    selected ? styles.selectionCheckboxChecked : null
                                                  ]}
                                                >
                                                  {selected ? (
                                                    <Ionicons
                                                      name="checkmark"
                                                      size={13}
                                                      color={palette.textPrimary}
                                                    />
                                                  ) : null}
                                                </View>
                                              ) : selected ? (
                                                <Ionicons
                                                  name="checkmark"
                                                  size={18}
                                                  color={subcategoryVisuals.color}
                                                />
                                              ) : null
                                            ) : null}
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
    </>
  );

  const selectionConfirmOverlay = selectionMode ? (
    <View pointerEvents="box-none" style={StyleSheet.absoluteFill}>
      <Animated.View
        pointerEvents={hasPendingSelection ? "auto" : "none"}
        style={[
          styles.confirmBar,
          {
            bottom: confirmVisibleBottom,
            opacity: confirmAnimation,
            transform: [
              {
                translateY: confirmAnimation.interpolate({
                  inputRange: [0, 1],
                  outputRange: [confirmHiddenTranslateY, 0]
                })
              }
            ]
          }
        ]}
      >
        <PrimaryButton
          label="Confirm selection"
          onPress={confirmSelection}
          disabled={!hasPendingSelection}
        />
      </Animated.View>
    </View>
  ) : null;

  return (
    <View style={styles.screenWrap}>
      {selectionMode ? (
        <PlanningHubScreen
          title="Select category"
          onBackPress={selectionReturnPath ? returnToSelectionOrigin : undefined}
          bottomOverlay={selectionConfirmOverlay}
        >
          {content}
        </PlanningHubScreen>
      ) : (
        <PlanningHubShell>
          <View style={styles.primaryScreen}>
          <HeaderShell
            preset="primaryTwoRowSearch"
            includeTopInset
            bleedHorizontal={PLANNING_HUB_CONTENT_PADDING_X}
            title="Categories"
            secondRow={
              <HeaderSearchSlot
                value={searchQuery}
                onChangeText={setSearchQuery}
                onClear={() => setSearchQuery("")}
                placeholder="Search categories, keywords, or brands"
                containerStyle={styles.primarySearchSlot}
              />
            }
          />
          <ScrollView
            contentContainerStyle={[
              styles.primaryScrollContent,
              {
                paddingTop: PLANNING_HUB_CONTENT_TOP_GAP,
                paddingBottom: getPlanningHubContentBottomInset(insets.bottom)
              }
            ]}
            showsVerticalScrollIndicator={false}
          >
            {content}
          </ScrollView>
          </View>
        </PlanningHubShell>
      )}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  screenWrap: {
    flex: 1
  },
  primaryScreen: {
    flex: 1
  },
  primaryScrollContent: {
    gap: spacing[16]
  },
  primarySearchSlot: {
    width: "100%"
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
    borderRadius: 6,
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
    borderRadius: 6,
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
    fontWeight: "600"
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
    backgroundColor: surfaces.muted,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[20],
    gap: spacing[8]
  },
  searchEmptyTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
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
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center"
  },
  domainTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "600"
  },
  rowActionRail: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
  },
  selectionCheckbox: {
    width: 20,
    height: 20,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.fieldStrong,
    alignItems: "center",
    justifyContent: "center"
  },
  selectionCheckboxChecked: {
    borderColor: "rgba(242,140,40,0.9)",
    backgroundColor: "rgba(242,140,40,0.2)"
  },
  selectionCheckboxPressed: {
    opacity: 0.88
  },
  domainCategoryDividerWrap: {
    alignItems: "center",
    paddingTop: 2,
    paddingBottom: spacing[4]
  },
  domainCategoryDivider: {
    width: "70%",
    height: 1,
    borderRadius: 6,
    backgroundColor: palette.border
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
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center"
  },
  categoryHeading: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  subcategoryList: {
    gap: spacing[8],
    paddingLeft: 10,
    paddingTop: 2
  },
  subcategoryRow: {
    minHeight: 50,
    borderRadius: 6,
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
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center"
  },
  subcategoryLabel: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  confirmBar: {
    position: "absolute",
    left: spacing[12],
    right: spacing[12]
  }
}));

