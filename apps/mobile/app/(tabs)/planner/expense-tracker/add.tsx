import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { LayoutAnimation, Platform, Pressable, ScrollView, StyleSheet, Text, UIManager, View } from "react-native";
import { ExpenseTrackerMiniAppScreen } from "../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { ExpenseTrackerSegmentedControl } from "../../../../src/components/expenseTracker/ExpenseTrackerSegmentedControl";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import { IconButton } from "../../../../src/components/ui/IconButton";
import { ModalSelectField } from "../../../../src/components/ui/ModalSelectField";
import { TextField } from "../../../../src/components/ui/TextField";
import {
  expenseTrackerPaymentSourceOptions,
  flattenVisibleExpenseTaxonomy,
  getExpenseTrackerSubcategoryVisual,
  getExpenseTrackerVisual
} from "../../../../src/features/expenseTracker/expenseTrackerModels";
import {
  buildExpenseTaxonomySearchIndex,
  normalizeExpenseTaxonomySearchText,
  searchExpenseTaxonomy
} from "../../../../src/features/expenseTracker/expenseTaxonomySearch";
import {
  useCreateExpenseTrackerEntryMutation,
  useExpenseTrackerEntriesQuery,
  useExpenseTrackerEntryDetailQuery,
  useExpenseTrackerTaxonomyQuery,
  useUpdateExpenseTrackerEntryMutation
} from "../../../../src/features/expenseTracker/useExpenseTracker";
import { showFlashMessage } from "../../../../src/lib/flashMessage";
import { useAuthSession } from "../../../../src/providers/AuthProvider";
import { palette, radius, spacing, typography } from "../../../../src/theme/tokens";
import type {
  CreateExpenseTrackerEntryRequest,
  ExpenseTaxonomyDomainDto,
  ExpenseTaxonomySubcategoryDto,
  ExpenseTrackerEntryDto
} from "../../../../src/types/api";

type FormErrors = Partial<Record<"title" | "amount" | "date" | "time" | "subcategoryId" | "paymentSource", string>>;

type TaxonomySelection = {
  domain: ExpenseTaxonomyDomainDto;
  category: ExpenseTaxonomyDomainDto["categories"][number];
  subcategory: ExpenseTaxonomySubcategoryDto;
};

function formatDateInput(date: Date) {
  return date.toISOString().slice(0, 10);
}

function formatTimeInput(date: Date) {
  return date.toISOString().slice(11, 16);
}

function normalize(value: string) {
  return value.trim().toLowerCase();
}

export default function ExpenseTrackerAddScreen() {
  const router = useRouter();
  const { session } = useAuthSession();
  const params = useLocalSearchParams<{
    entryId?: string;
    duplicateId?: string;
    subcategoryId?: string;
    defaultStatus?: "planned" | "completed";
    recurring?: string;
    focusDomainId?: string;
  }>();
  const entryId = typeof params.entryId === "string" ? params.entryId : "";
  const duplicateId = typeof params.duplicateId === "string" ? params.duplicateId : "";
  const initialSubcategoryId = typeof params.subcategoryId === "string" ? Number(params.subcategoryId) : null;
  const focusDomainId = typeof params.focusDomainId === "string" ? Number(params.focusDomainId) : null;
  const sourceEntryId = entryId || duplicateId;
  const detailQuery = useExpenseTrackerEntryDetailQuery(sourceEntryId);
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const taxonomyQuery = useExpenseTrackerTaxonomyQuery();
  const createMutation = useCreateExpenseTrackerEntryMutation();
  const updateMutation = useUpdateExpenseTrackerEntryMutation();
  const [selectedSubcategoryId, setSelectedSubcategoryId] = useState<number | null>(Number.isFinite(initialSubcategoryId) ? initialSubcategoryId : null);
  const [title, setTitle] = useState("");
  const [amount, setAmount] = useState("");
  const [paymentSource, setPaymentSource] = useState("Cash");
  const [dateValue, setDateValue] = useState(formatDateInput(new Date()));
  const [timeValue, setTimeValue] = useState(formatTimeInput(new Date()));
  const [status, setStatus] = useState<"planned" | "completed">(params.defaultStatus === "planned" ? "planned" : "completed");
  const [isRecurring, setIsRecurring] = useState(params.recurring === "true");
  const [merchant, setMerchant] = useState("");
  const [notes, setNotes] = useState("");
  const [tagsText, setTagsText] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [hasHydrated, setHasHydrated] = useState(false);
  const [showMoreDetails, setShowMoreDetails] = useState(Boolean(entryId || duplicateId));
  const [searchQuery, setSearchQuery] = useState("");
  const [expandedDomainId, setExpandedDomainId] = useState<number | null>(focusDomainId ?? null);
  const [expandedCategoryIds, setExpandedCategoryIds] = useState<Record<number, number | null>>({});
  const scrollViewRef = useRef<ScrollView | null>(null);
  const composerSectionTopRef = useRef(0);

  const isEditing = Boolean(entryId);
  const currency = session?.user.preferredCurrency?.trim().toUpperCase() || "EUR";
  const allEntries = entriesQuery.data ?? [];
  const visibleDomains = taxonomyQuery.data?.domains ?? [];
  const flattenedSelections = useMemo(() => flattenVisibleExpenseTaxonomy(visibleDomains), [visibleDomains]);
  const selectionBySubcategoryId = useMemo(
    () => new Map(flattenedSelections.map((item) => [item.subcategory.id, item] as const)),
    [flattenedSelections]
  );
  const selectedSelection = selectedSubcategoryId ? selectionBySubcategoryId.get(selectedSubcategoryId) ?? null : null;
  const searchIndex = useMemo(() => buildExpenseTaxonomySearchIndex(visibleDomains), [visibleDomains]);
  const searchResults = useMemo(() => searchExpenseTaxonomy(searchIndex, searchQuery), [searchIndex, searchQuery]);
  const hasSearchQuery = normalizeExpenseTaxonomySearchText(searchQuery).length > 0;
  const orderedDomains = useMemo(() => {
    if (!focusDomainId) {
      return visibleDomains;
    }

    return [...visibleDomains].sort((left, right) => {
      if (left.id === focusDomainId) {
        return -1;
      }
      if (right.id === focusDomainId) {
        return 1;
      }
      return left.sortOrder - right.sortOrder;
    });
  }, [focusDomainId, visibleDomains]);

  useEffect(() => {
    if (!selectedSelection) {
      return;
    }

    setExpandedDomainId(selectedSelection.domain.id);
    setExpandedCategoryIds((current) => ({
      ...current,
      [selectedSelection.domain.id]: selectedSelection.category.id
    }));
  }, [selectedSelection]);

  const toggleDomain = (domainId: number) => {
    LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
    setExpandedDomainId((current) => (current === domainId ? null : domainId));
  };

  const toggleCategory = (domainId: number, categoryId: number) => {
    LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
    setExpandedDomainId(domainId);
    setExpandedCategoryIds((current) => ({
      ...current,
      [domainId]: current[domainId] === categoryId ? null : categoryId
    }));
  };

  useEffect(() => {
    if (Platform.OS === "android" && UIManager.setLayoutAnimationEnabledExperimental) {
      UIManager.setLayoutAnimationEnabledExperimental(true);
    }
  }, []);

  useEffect(() => {
    if (!sourceEntryId || !detailQuery.data || hasHydrated) {
      return;
    }

    const entry = detailQuery.data;
    const occurredAt = new Date(entry.occurredAtUtc);
    setSelectedSubcategoryId(entry.subcategoryId ?? selectedSubcategoryId);
    setTitle(duplicateId ? `${entry.title} copy` : entry.title);
    setAmount(entry.amount.toFixed(2));
    setPaymentSource(entry.paymentSource);
    setDateValue(formatDateInput(occurredAt));
    setTimeValue(formatTimeInput(occurredAt));
    setStatus(entry.status);
    setIsRecurring(entry.isRecurring);
    setMerchant(entry.merchant ?? "");
    setNotes(entry.notes ?? "");
    setTagsText(entry.tags.join(", "));
    setShowMoreDetails(true);
    setHasHydrated(true);
  }, [detailQuery.data, duplicateId, hasHydrated, selectedSubcategoryId, sourceEntryId]);

  useEffect(() => {
    if (!selectedSubcategoryId) {
      return;
    }

    const timeoutId = setTimeout(() => {
      scrollViewRef.current?.scrollTo({
        y: Math.max(composerSectionTopRef.current - 12, 0),
        animated: true
      });
    }, 180);

    return () => clearTimeout(timeoutId);
  }, [selectedSubcategoryId]);

  const recentSelections = useMemo(() => {
    const unique = new Map<number, number>();
    allEntries.forEach((entry) => {
      if (!entry.subcategoryId) {
        return;
      }
      unique.set(entry.subcategoryId, (unique.get(entry.subcategoryId) ?? 0) + 1);
    });

    return Array.from(unique.entries())
      .sort((left, right) => right[1] - left[1])
      .slice(0, 6)
      .map(([subcategoryId]) => selectionBySubcategoryId.get(subcategoryId))
      .filter((item): item is TaxonomySelection => Boolean(item));
  }, [allEntries, selectionBySubcategoryId]);

  const titleSuggestions = useMemo(() => {
    const query = normalize(title);
    const source = query
      ? allEntries.filter((entry) => normalize(entry.title).includes(query) || normalize(entry.merchant ?? "").includes(query))
      : allEntries.slice(0, 4);

    const unique = new Map<string, ExpenseTrackerEntryDto>();
    source.forEach((entry) => {
      const key = `${normalize(entry.title)}|${normalize(entry.merchant ?? "")}`;
      if (!unique.has(key)) {
        unique.set(key, entry);
      }
    });

    return Array.from(unique.values()).slice(0, 4);
  }, [allEntries, title]);

  const smartSuggestion = useMemo(() => {
    const merchantKey = normalize(merchant);
    const titleKey = normalize(title);

    return allEntries.find((entry) => {
      if (merchantKey && normalize(entry.merchant ?? "") === merchantKey) {
        return true;
      }
      if (titleKey && normalize(entry.title) === titleKey) {
        return true;
      }
      return false;
    }) ?? null;
  }, [allEntries, merchant, title]);

  const validate = () => {
    const nextErrors: FormErrors = {};
    const parsedAmount = Number(amount);

    if (!selectedSubcategoryId) {
      nextErrors.subcategoryId = "Select a category first.";
    }
    if (!title.trim()) {
      nextErrors.title = "Title is required.";
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      nextErrors.amount = "Enter an amount greater than zero.";
    }
    if (!paymentSource.trim()) {
      nextErrors.paymentSource = "Pick a payment source.";
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(dateValue)) {
      nextErrors.date = "Use YYYY-MM-DD.";
    }
    if (!/^\d{2}:\d{2}$/.test(timeValue)) {
      nextErrors.time = "Use HH:mm.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const applySuggestion = (entry: ExpenseTrackerEntryDto) => {
    if (entry.subcategoryId) {
      setSelectedSubcategoryId(entry.subcategoryId);
    }
    setTitle(entry.title);
    setPaymentSource(entry.paymentSource);
    setMerchant(entry.merchant ?? merchant);
    setIsRecurring(entry.isRecurring);
    if (!amount) {
      setAmount(entry.amount.toFixed(2));
    }
  };

  const handleSave = async () => {
    if (!validate()) {
      return;
    }

    const occurredAt = new Date(`${dateValue}T${timeValue}:00`);
    const payload: CreateExpenseTrackerEntryRequest = {
      title: title.trim(),
      amount: Number(Number(amount).toFixed(2)),
      currency,
      subcategoryId: selectedSubcategoryId!,
      paymentSource,
      occurredAtUtc: occurredAt.toISOString(),
      notes: notes.trim() || null,
      tags: tagsText
        .split(",")
        .map((tag) => tag.trim())
        .filter(Boolean),
      status,
      isRecurring,
      merchant: merchant.trim() || null
    };

    if (isEditing && entryId) {
      await updateMutation.mutateAsync({ entryId, payload });
      showFlashMessage("Expense updated.", { tone: "success" });
      router.replace("/(tabs)/planner/expense-tracker/overview" as never);
      return;
    }

    await createMutation.mutateAsync(payload);
    showFlashMessage("Expense added.", { tone: "success" });
    router.replace("/(tabs)/planner/expense-tracker/overview" as never);
  };

  const paymentSourceOptions = expenseTrackerPaymentSourceOptions.map((option) => ({ label: option.label, value: option.value }));

  const handleSubcategorySelection = (subcategoryId: number) => {
    const selection = selectionBySubcategoryId.get(subcategoryId);
    LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
    if (selection) {
      setExpandedDomainId(selection.domain.id);
      setExpandedCategoryIds((current) => ({
        ...current,
        [selection.domain.id]: selection.category.id
      }));
    }
    setSearchQuery("");
    setSelectedSubcategoryId(subcategoryId);
  };

  return (
    <ExpenseTrackerMiniAppScreen title="Add Expense" scrollViewRef={scrollViewRef}>
      {taxonomyQuery.isError ? (
        <ErrorState
          title="Could not load categories"
          message={taxonomyQuery.error.message}
          onRetry={() => {
            void taxonomyQuery.refetch();
          }}
        />
      ) : null}

      {detailQuery.isError ? (
        <ErrorState
          title="Could not load entry"
          message={detailQuery.error.message}
          onRetry={() => {
            void detailQuery.refetch();
          }}
        />
      ) : null}

      {createMutation.isError || updateMutation.isError ? (
        <ErrorState
          title="Could not save entry"
          message={createMutation.error?.message ?? updateMutation.error?.message ?? "Please try again."}
          onRetry={() => {
            void handleSave();
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
        {!hasSearchQuery && recentSelections.length > 0 ? (
          <View style={styles.recentWrap}>
            <Text style={styles.recentLabel}>Recent categories</Text>
            <View style={styles.recentRow}>
              {recentSelections.map((item) => {
                const selected = selectedSubcategoryId === item.subcategory.id;
                return (
                  <Pressable
                    key={item.subcategory.id}
                    style={[styles.recentChip, selected ? styles.recentChipSelected : null]}
                    onPress={() => handleSubcategorySelection(item.subcategory.id)}
                  >
                    <Text style={[styles.recentChipLabel, selected ? styles.recentChipLabelSelected : null]} numberOfLines={1}>
                      {item.subcategory.name}
                    </Text>
                  </Pressable>
                );
              })}
            </View>
          </View>
        ) : null}

        {hasSearchQuery ? (
          searchResults.length > 0 ? (
            <View style={styles.searchResultsList}>
              {searchResults.map((result) => {
                const selected = selectedSubcategoryId === result.item.subcategoryId;
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
                      pressed ? styles.searchResultRowPressed : null,
                      selected ? styles.searchResultRowSelected : null,
                      {
                        borderColor: selected ? visual.color : palette.border,
                        backgroundColor: selected ? `${visual.color}18` : "rgba(18,36,58,0.82)"
                      }
                    ]}
                    onPress={() => handleSubcategorySelection(result.item.subcategoryId)}
                  >
                    <View style={[styles.searchResultIconWrap, { backgroundColor: `${visual.color}20` }]}>
                      <Ionicons name={visual.icon as keyof typeof Ionicons.glyphMap} size={16} color={visual.color} />
                    </View>
                    <View style={styles.searchResultCopy}>
                      <Text style={styles.searchResultTitle}>{result.item.subcategoryName}</Text>
                      <Text style={styles.searchResultPath}>{result.item.categoryName} • {result.item.domainName}</Text>
                    </View>
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
            {orderedDomains.map((domain) => {
              const domainVisuals = getExpenseTrackerVisual({ domainId: domain.id, categoryId: null });
              const isDomainExpanded = expandedDomainId === domain.id;
              const expandedCategoryId = expandedCategoryIds[domain.id] ?? null;

              return (
                <View key={domain.id} style={styles.domainSection}>
                  <Pressable
                    onPress={() => toggleDomain(domain.id)}
                    style={({ pressed }) => [styles.domainButton, pressed ? styles.domainButtonPressed : null, isDomainExpanded ? styles.domainButtonExpanded : null]}
                  >
                    <View style={styles.domainButtonLeft}>
                      <View style={[styles.domainIconWrap, { backgroundColor: `${domainVisuals.color}18` }]}>
                        <Ionicons name={domainVisuals.icon as keyof typeof Ionicons.glyphMap} size={16} color={domainVisuals.color} />
                      </View>
                      <Text style={styles.domainTitle}>{domain.name}</Text>
                    </View>
                    <Ionicons
                      name={isDomainExpanded ? "chevron-up" : "chevron-down"}
                      size={18}
                      color={palette.textSecondary}
                    />
                  </Pressable>

                  {isDomainExpanded ? (
                    <>
                      <View style={styles.domainCategoryDividerWrap}>
                        <View style={styles.domainCategoryDivider} />
                      </View>
                      <View style={styles.categorySectionList}>
                        {domain.categories.map((category) => {
                          const categoryVisuals = getExpenseTrackerVisual({ domainId: domain.id, categoryId: category.id });
                          const isCategoryExpanded = expandedCategoryId === category.id;

                          return (
                            <View key={category.id} style={styles.categorySection}>
                              <Pressable
                                onPress={() => toggleCategory(domain.id, category.id)}
                                style={({ pressed }) => [
                                  styles.categoryButton,
                                  pressed ? styles.categoryButtonPressed : null,
                                  isCategoryExpanded ? styles.categoryButtonExpanded : null
                                ]}
                              >
                                <View style={styles.categoryButtonLeft}>
                                  <View style={[styles.categoryAccordionIconWrap, { backgroundColor: `${categoryVisuals.color}18` }]}>
                                    <Ionicons name={categoryVisuals.icon as keyof typeof Ionicons.glyphMap} size={15} color={categoryVisuals.color} />
                                  </View>
                                  <Text style={styles.categoryHeading}>{category.name}</Text>
                                </View>
                                <Ionicons
                                  name={isCategoryExpanded ? "chevron-up" : "chevron-down"}
                                  size={17}
                                  color={palette.textSecondary}
                                />
                              </Pressable>

                              {isCategoryExpanded ? (
                                <View style={styles.subcategoryList}>
                                  {category.subcategories.map((subcategory) => {
                                    const selected = selectedSubcategoryId === subcategory.id;
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
                                          pressed ? styles.subcategoryRowPressed : null,
                                          selected ? styles.subcategoryRowSelected : null,
                                          {
                                            borderColor: selected ? subcategoryVisuals.color : palette.border,
                                            backgroundColor: selected ? `${subcategoryVisuals.color}18` : "rgba(18,36,58,0.82)"
                                          }
                                        ]}
                                        onPress={() => handleSubcategorySelection(subcategory.id)}
                                      >
                                        <View style={[styles.subcategoryIconWrap, { backgroundColor: `${subcategoryVisuals.color}20` }]}>
                                          <Ionicons
                                            name={subcategoryVisuals.icon as keyof typeof Ionicons.glyphMap}
                                            size={16}
                                            color={subcategoryVisuals.color}
                                          />
                                        </View>
                                        <Text style={styles.subcategoryLabel}>{subcategory.name}</Text>
                                        {selected ? <Ionicons name="checkmark" size={18} color={subcategoryVisuals.color} /> : null}
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

      {selectedSelection ? (
        <View
          onLayout={(event) => {
            composerSectionTopRef.current = event.nativeEvent.layout.y;
          }}
          style={styles.composerSection}
        >
          <GlassCard style={styles.composerHero}>
            <View style={styles.selectedCategoryRow}>
              <View style={styles.selectedCopyWrap}>
                <Text style={styles.heroLabel}>{selectedSelection.category.name}</Text>
                <Text style={styles.selectedCategoryLabel}>{selectedSelection.subcategory.name}</Text>
              </View>
              <View style={styles.composerHeaderActions}>
                <Pressable onPress={() => setSelectedSubcategoryId(null)} style={styles.changeCategoryButton}>
                  <Text style={styles.changeCategoryLabel}>Change</Text>
                </Pressable>
                <IconButton
                  onPress={() => {
                    void handleSave();
                  }}
                  icon={<Ionicons name="checkmark" size={18} color={palette.textPrimary} />}
                />
              </View>
            </View>

            <TextField
              label={`Amount (${currency})`}
              showLabel={false}
              value={amount}
              onChangeText={setAmount}
              placeholder="0.00"
              keyboardType="decimal-pad"
              error={errors.amount}
              style={styles.amountInput}
              forceFocused={Boolean(amount)}
            />

            <ExpenseTrackerSegmentedControl
              label="Status"
              value={status}
              options={[
                { label: "Completed", value: "completed" },
                { label: "Planned", value: "planned" }
              ]}
              onChange={setStatus}
            />
          </GlassCard>

          <GlassCard style={styles.formCard}>
            <TextField
              label="Merchant or title"
              value={title}
              onChangeText={setTitle}
              placeholder="Tesco groceries, rent, coffee..."
              error={errors.title}
            />

            {smartSuggestion ? (
              <Pressable style={styles.smartSuggestionCard} onPress={() => applySuggestion(smartSuggestion)}>
                <View style={styles.smartSuggestionTextWrap}>
                  <Text style={styles.smartSuggestionLabel}>Smart fill</Text>
                  <Text style={styles.smartSuggestionText}>
                    Reuse a recent category and payment setup from your last similar entry.
                  </Text>
                </View>
                <Ionicons name="sparkles-outline" size={18} color={palette.accent} />
              </Pressable>
            ) : null}

            <View style={styles.suggestionWrap}>
              {titleSuggestions.map((entry) => (
                <Pressable key={`${entry.id}-shortcut`} style={styles.suggestionChip} onPress={() => applySuggestion(entry)}>
                  <Text style={styles.suggestionChipText} numberOfLines={1}>{entry.title}</Text>
                </Pressable>
              ))}
            </View>

            <Pressable
              style={({ pressed }) => [styles.expandButton, pressed ? styles.expandButtonPressed : null]}
              onPress={() => {
                LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
                setShowMoreDetails((current) => !current);
              }}
            >
              <View>
                <Text style={styles.expandTitle}>More details</Text>
                <Text style={styles.expandSubtitle}>Payment source, date, notes, recurring, tags, and merchant context.</Text>
              </View>
              <Ionicons name={showMoreDetails ? "chevron-up" : "chevron-down"} size={18} color={palette.textSecondary} />
            </Pressable>

            {showMoreDetails ? (
              <View style={styles.moreDetailsSection}>
                <View style={styles.splitRow}>
                  <View style={styles.splitField}>
                    <TextField
                      label="Date"
                      value={dateValue}
                      onChangeText={setDateValue}
                      placeholder="YYYY-MM-DD"
                      error={errors.date}
                    />
                  </View>
                  <View style={styles.splitField}>
                    <TextField
                      label="Time"
                      value={timeValue}
                      onChangeText={setTimeValue}
                      placeholder="HH:mm"
                      error={errors.time}
                    />
                  </View>
                </View>

                <ModalSelectField
                  label="Payment source"
                  value={paymentSource}
                  options={paymentSourceOptions}
                  onChange={setPaymentSource}
                />

                <TextField
                  label="Merchant / store"
                  value={merchant}
                  onChangeText={setMerchant}
                  placeholder="Tesco, AIB, Vodafone..."
                />

                <TextField
                  label="Notes"
                  value={notes}
                  onChangeText={setNotes}
                  placeholder="Quick context you may want later"
                  multiline
                  style={styles.notesField}
                />

                <TextField
                  label="Tags"
                  value={tagsText}
                  onChangeText={setTagsText}
                  placeholder="home, monthly, family"
                />

                <Pressable
                  onPress={() => setIsRecurring((current) => !current)}
                  style={({ pressed }) => [styles.toggleRow, pressed ? styles.toggleRowPressed : null, isRecurring ? styles.toggleRowActive : null]}
                >
                  <View>
                    <Text style={styles.toggleTitle}>Recurring</Text>
                    <Text style={styles.toggleSubtitle}>Great for bills, subscriptions, and repeat purchases.</Text>
                  </View>
                  <View style={[styles.checkbox, isRecurring ? styles.checkboxChecked : null]}>
                    {isRecurring ? <Ionicons name="checkmark" size={16} color={palette.textPrimary} /> : null}
                  </View>
                </Pressable>
              </View>
            ) : null}
          </GlassCard>
        </View>
      ) : null}
    </ExpenseTrackerMiniAppScreen>
  );
}

const styles = StyleSheet.create({
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
  searchWrap: {
    gap: spacing[8]
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
  recentWrap: {
    gap: 6
  },
  recentLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    opacity: 0.8
  },
  recentRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 6
  },
  recentChip: {
    borderRadius: 999,
    borderWidth: 1,
    borderColor: "rgba(213, 229, 255, 0.1)",
    backgroundColor: "rgba(18,36,58,0.44)",
    paddingHorizontal: 10,
    paddingVertical: 6,
    maxWidth: 150
  },
  recentChipSelected: {
    borderColor: "rgba(127,174,255,0.28)",
    backgroundColor: "rgba(47,107,255,0.14)"
  },
  recentChipLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontSize: 11,
    lineHeight: 14
  },
  recentChipLabelSelected: {
    color: palette.textPrimary
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
  domainButtonExpanded: {},
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
  categoryButtonExpanded: {},
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
  composerSection: {
    gap: 14,
    paddingBottom: spacing[24]
  },
  composerHero: {
    gap: 14
  },
  selectedCategoryRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  selectedCopyWrap: {
    flex: 1
  },
  composerHeaderActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  heroLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  selectedCategoryLabel: {
    marginTop: 4,
    color: palette.textPrimary,
    ...typography.title2
  },
  changeCategoryButton: {
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[8]
  },
  changeCategoryLabel: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700"
  },
  amountInput: {
    minHeight: 68,
    fontSize: 30,
    lineHeight: 36,
    fontWeight: "700",
    letterSpacing: 0.4,
    fontVariant: ["tabular-nums"]
  },
  formCard: {
    gap: 14
  },
  smartSuggestionCard: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12],
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: "rgba(111,215,255,0.2)",
    backgroundColor: "rgba(111,215,255,0.1)",
    padding: 14
  },
  smartSuggestionTextWrap: {
    flex: 1,
    gap: 4
  },
  smartSuggestionLabel: {
    color: palette.accent,
    ...typography.caption,
    fontWeight: "700"
  },
  smartSuggestionText: {
    color: palette.textPrimary,
    ...typography.body2
  },
  suggestionWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  suggestionChip: {
    borderRadius: 999,
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.2)",
    backgroundColor: "rgba(47,107,255,0.14)",
    paddingHorizontal: 10,
    paddingVertical: spacing[8],
    maxWidth: "100%"
  },
  suggestionChipText: {
    color: palette.textPrimary,
    ...typography.caption
  },
  expandButton: {
    minHeight: 56,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.68)",
    paddingHorizontal: 14,
    paddingVertical: spacing[12],
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: spacing[12]
  },
  expandButtonPressed: {
    opacity: 0.94
  },
  expandTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  expandSubtitle: {
    marginTop: 2,
    color: palette.textSecondary,
    ...typography.caption,
    maxWidth: 250
  },
  moreDetailsSection: {
    gap: 14
  },
  splitRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  splitField: {
    flex: 1
  },
  notesField: {
    minHeight: 108,
    textAlignVertical: "top",
    paddingTop: spacing[12]
  },
  toggleRow: {
    minHeight: 58,
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.68)",
    paddingHorizontal: 14,
    paddingVertical: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  toggleRowActive: {
    borderColor: "rgba(104,215,169,0.3)",
    backgroundColor: "rgba(104,215,169,0.1)"
  },
  toggleRowPressed: {
    opacity: 0.94
  },
  toggleTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  toggleSubtitle: {
    marginTop: 2,
    color: palette.textSecondary,
    ...typography.caption,
    maxWidth: 250
  },
  checkbox: {
    width: 24,
    height: 24,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: palette.border,
    alignItems: "center",
    justifyContent: "center"
  },
  checkboxChecked: {
    borderColor: "rgba(104,215,169,0.46)",
    backgroundColor: "rgba(104,215,169,0.32)"
  }
});








