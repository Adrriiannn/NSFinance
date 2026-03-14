import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { LayoutAnimation, Platform, Pressable, ScrollView, StyleSheet, Text, UIManager, View } from "react-native";
import { ExpenseTrackerMiniAppScreen } from "../../../../src/components/expenseTracker/ExpenseTrackerMiniAppScreen";
import { ExpenseTrackerSegmentedControl } from "../../../../src/components/expenseTracker/ExpenseTrackerSegmentedControl";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../../src/components/ui/GlassCard";
import { ModalSelectField } from "../../../../src/components/ui/ModalSelectField";
import { IconButton } from "../../../../src/components/ui/IconButton";
import { TextField } from "../../../../src/components/ui/TextField";
import {
  expenseTrackerCategoryOptions,
  expenseTrackerPaymentSourceOptions
} from "../../../../src/features/expenseTracker/expenseTrackerModels";
import {
  useCreateExpenseTrackerEntryMutation,
  useExpenseTrackerEntriesQuery,
  useExpenseTrackerEntryDetailQuery,
  useUpdateExpenseTrackerEntryMutation
} from "../../../../src/features/expenseTracker/useExpenseTracker";
import { showFlashMessage } from "../../../../src/lib/flashMessage";
import { useAuthSession } from "../../../../src/providers/AuthProvider";
import { palette, radius, spacing, typography } from "../../../../src/theme/tokens";
import type { CreateExpenseTrackerEntryRequest, ExpenseTrackerEntryDto } from "../../../../src/types/api";

type FormErrors = Partial<Record<"title" | "amount" | "date" | "time" | "category" | "paymentSource", string>>;

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
    category?: string;
    defaultStatus?: "planned" | "completed";
    recurring?: string;
  }>();
  const entryId = typeof params.entryId === "string" ? params.entryId : "";
  const duplicateId = typeof params.duplicateId === "string" ? params.duplicateId : "";
  const sourceEntryId = entryId || duplicateId;
  const detailQuery = useExpenseTrackerEntryDetailQuery(sourceEntryId);
  const entriesQuery = useExpenseTrackerEntriesQuery();
  const createMutation = useCreateExpenseTrackerEntryMutation();
  const updateMutation = useUpdateExpenseTrackerEntryMutation();
  const [selectedCategory, setSelectedCategory] = useState<string | null>(typeof params.category === "string" ? params.category : null);
  const [title, setTitle] = useState("");
  const [amount, setAmount] = useState("");
  const [paymentSource, setPaymentSource] = useState("Cash");
  const [dateValue, setDateValue] = useState(formatDateInput(new Date()));
  const [timeValue, setTimeValue] = useState(formatTimeInput(new Date()));
  const [status, setStatus] = useState<"planned" | "completed">(
    params.defaultStatus === "planned" ? "planned" : "completed"
  );
  const [isRecurring, setIsRecurring] = useState(params.recurring === "true");
  const [merchant, setMerchant] = useState("");
  const [notes, setNotes] = useState("");
  const [tagsText, setTagsText] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [hasHydrated, setHasHydrated] = useState(false);
  const [showMoreDetails, setShowMoreDetails] = useState(Boolean(entryId || duplicateId));
  const scrollViewRef = useRef<ScrollView | null>(null);
  const composerSectionTopRef = useRef(0);

  const isEditing = Boolean(entryId);
  const currency = session?.user.preferredCurrency?.trim().toUpperCase() || "EUR";
  const allEntries = entriesQuery.data ?? [];

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
    setSelectedCategory(entry.category);
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
  }, [detailQuery.data, duplicateId, hasHydrated, sourceEntryId]);

  useEffect(() => {
    if (!selectedCategory) {
      return;
    }

    const timeoutId = setTimeout(() => {
      scrollViewRef.current?.scrollTo({
        y: Math.max(composerSectionTopRef.current - 12, 0),
        animated: true
      });
    }, 180);

    return () => clearTimeout(timeoutId);
  }, [selectedCategory]);

  const recentCategories = useMemo(() => {
    const unique = new Map<string, number>();
    allEntries.forEach((entry) => {
      if (!unique.has(entry.category)) {
        unique.set(entry.category, 1);
      } else {
        unique.set(entry.category, (unique.get(entry.category) ?? 0) + 1);
      }
    });

    return Array.from(unique.entries())
      .sort((left, right) => right[1] - left[1])
      .slice(0, 4)
      .map(([category]) => category);
  }, [allEntries]);

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

    if (!selectedCategory) {
      nextErrors.category = "Select a category first.";
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
    setSelectedCategory(entry.category);
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
      category: selectedCategory!,
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

  const handleCategorySelection = (category: string) => {
    LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
    setSelectedCategory(category);
  };

  return (
    <ExpenseTrackerMiniAppScreen title="Add Expense" scrollViewRef={scrollViewRef}>
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

      <GlassCard style={styles.categoryLauncherCard}>
        {recentCategories.length > 0 ? (
          <View style={styles.recentWrap}>
            <Text style={styles.recentLabel}>Recent categories</Text>
            <View style={styles.recentRow}>
              {recentCategories.map((category) => {
                const option = expenseTrackerCategoryOptions.find((item) => item.value === category);
                if (!option) {
                  return null;
                }
                const selected = selectedCategory === option.value;
                return (
                  <Pressable
                    key={option.value}
                    style={[styles.recentChip, selected ? styles.recentChipSelected : null]}
                    onPress={() => handleCategorySelection(option.value)}
                  >
                    <Text style={[styles.recentChipLabel, selected ? styles.recentChipLabelSelected : null]}>{option.label}</Text>
                  </Pressable>
                );
              })}
            </View>
          </View>
        ) : null}

        <View style={styles.categoryGrid}>
          {expenseTrackerCategoryOptions.map((option) => {
            const selected = selectedCategory === option.value;
            return (
              <Pressable
                key={option.value}
                style={[styles.categoryTile, selected ? styles.categoryTileSelected : null, { backgroundColor: `${option.color}18` }]}
                onPress={() => handleCategorySelection(option.value)}
              >
                <View style={[styles.categoryIconWrap, { backgroundColor: `${option.color}22` }]}>
                  <Ionicons name={option.icon as keyof typeof Ionicons.glyphMap} size={18} color={option.color} />
                </View>
                <Text style={styles.categoryTileLabel}>{option.label}</Text>
              </Pressable>
            );
          })}
        </View>
      </GlassCard>

      {selectedCategory ? (
        <View
          onLayout={(event) => {
            composerSectionTopRef.current = event.nativeEvent.layout.y;
          }}
          style={styles.composerSection}
        >
          <GlassCard style={styles.composerHero}>
            <View style={styles.selectedCategoryRow}>
              <View>
                <Text style={styles.heroLabel}>Selected category</Text>
                <Text style={styles.selectedCategoryLabel}>{selectedCategory}</Text>
              </View>
              <View style={styles.composerHeaderActions}>
                <Pressable
                  onPress={() => setSelectedCategory(null)}
                  style={styles.changeCategoryButton}
                >
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
                    Reuse {smartSuggestion.category} | {smartSuggestion.paymentSource} from your last similar entry.
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
  categoryLauncherCard: {
    gap: spacing[16]
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
    paddingVertical: 6
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
  categoryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[12]
  },
  categoryTile: {
    width: "31%",
    minHeight: 110,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: "rgba(226,236,255,0.14)",
    padding: spacing[12],
    justifyContent: "space-between"
  },
  categoryTileSelected: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.16)"
  },
  categoryIconWrap: {
    width: 40,
    height: 40,
    borderRadius: 16,
    alignItems: "center",
    justifyContent: "center"
  },
  categoryTileLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
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
