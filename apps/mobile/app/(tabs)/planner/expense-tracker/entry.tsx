import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../../src/components/feedback/ErrorState";
import { ScreenContainer } from "../../../../src/components/ui/ScreenContainer";
import { SelectField } from "../../../../src/components/ui/SelectField";
import { TextField } from "../../../../src/components/ui/TextField";
import {
  expenseTrackerCategoryOptions,
  expenseTrackerPaymentSourceOptions,
  expenseTrackerStatusOptions
} from "../../../../src/features/expenseTracker/expenseTrackerModels";
import {
  useCreateExpenseTrackerEntryMutation,
  useExpenseTrackerEntryDetailQuery,
  useUpdateExpenseTrackerEntryMutation
} from "../../../../src/features/expenseTracker/useExpenseTracker";
import { showFlashMessage } from "../../../../src/lib/flashMessage";
import { useAuthSession } from "../../../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../../../src/theme/tokens";
import type { CreateExpenseTrackerEntryRequest } from "../../../../src/types/api";

type FormErrors = Partial<Record<"title" | "amount" | "date" | "time" | "category" | "paymentSource", string>>;

function formatDateInput(date: Date) {
  return date.toISOString().slice(0, 10);
}

function formatTimeInput(date: Date) {
  return date.toISOString().slice(11, 16);
}

export default function ExpenseTrackerEntryScreen() {
  const router = useRouter();
  const { session } = useAuthSession();
  const params = useLocalSearchParams<{ entryId?: string; duplicateId?: string }>();
  const entryId = typeof params.entryId === "string" ? params.entryId : "";
  const duplicateId = typeof params.duplicateId === "string" ? params.duplicateId : "";
  const sourceEntryId = entryId || duplicateId;
  const detailQuery = useExpenseTrackerEntryDetailQuery(sourceEntryId);
  const createMutation = useCreateExpenseTrackerEntryMutation();
  const updateMutation = useUpdateExpenseTrackerEntryMutation();
  const [mode, setMode] = useState<"quick" | "full">(entryId ? "full" : "quick");
  const [title, setTitle] = useState("");
  const [amount, setAmount] = useState("");
  const [category, setCategory] = useState("Groceries");
  const [paymentSource, setPaymentSource] = useState("Cash");
  const [dateValue, setDateValue] = useState(formatDateInput(new Date()));
  const [timeValue, setTimeValue] = useState(formatTimeInput(new Date()));
  const [status, setStatus] = useState<"planned" | "completed">("completed");
  const [isRecurring, setIsRecurring] = useState(false);
  const [merchant, setMerchant] = useState("");
  const [notes, setNotes] = useState("");
  const [tagsText, setTagsText] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [hasHydrated, setHasHydrated] = useState(false);

  const isEditing = Boolean(entryId);
  const currency = session?.user.preferredCurrency?.trim().toUpperCase() || "EUR";

  useEffect(() => {
    if (!sourceEntryId || !detailQuery.data || hasHydrated) {
      return;
    }

    const entry = detailQuery.data;
    const occurredAt = new Date(entry.occurredAtUtc);
    setTitle(duplicateId ? `${entry.title} copy` : entry.title);
    setAmount(entry.amount.toFixed(2));
    setCategory(entry.category);
    setPaymentSource(entry.paymentSource);
    setDateValue(formatDateInput(occurredAt));
    setTimeValue(formatTimeInput(occurredAt));
    setStatus(entry.status);
    setIsRecurring(entry.isRecurring);
    setMerchant(entry.merchant ?? "");
    setNotes(entry.notes ?? "");
    setTagsText(entry.tags.join(", "));
    setMode("full");
    setHasHydrated(true);
  }, [detailQuery.data, duplicateId, hasHydrated, sourceEntryId]);

  const categoryOptions = useMemo(
    () => expenseTrackerCategoryOptions.map((option) => ({ label: option.label, value: option.value })),
    []
  );
  const paymentSourceOptions = useMemo(
    () => expenseTrackerPaymentSourceOptions.map((option) => ({ label: option.label, value: option.value })),
    []
  );

  const validate = () => {
    const nextErrors: FormErrors = {};
    const parsedAmount = Number(amount);

    if (!title.trim()) {
      nextErrors.title = "Title is required.";
    }

    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      nextErrors.amount = "Enter an amount greater than zero.";
    }

    if (!/^\d{4}-\d{2}-\d{2}$/.test(dateValue)) {
      nextErrors.date = "Use YYYY-MM-DD.";
    }

    if (!/^\d{2}:\d{2}$/.test(timeValue)) {
      nextErrors.time = "Use HH:mm.";
    }

    if (!category.trim()) {
      nextErrors.category = "Pick a category.";
    }

    if (!paymentSource.trim()) {
      nextErrors.paymentSource = "Pick a payment source.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
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
      category,
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
      router.back();
      return;
    }

    await createMutation.mutateAsync(payload);
    showFlashMessage(isEditing ? "Expense updated." : "Expense added.", { tone: "success" });
    router.back();
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset>
      <View style={styles.headerRow}>
        <View>
          <Text style={styles.title}>{isEditing ? "Edit expense" : "Add expense"}</Text>
          <Text style={styles.subtitle}>Track cash spending, bills, subscriptions, and planned purchases without spreadsheet clutter.</Text>
        </View>
        <Pressable onPress={() => router.back()} style={styles.closeButton}>
          <Ionicons name="close" size={22} color={palette.textSecondary} />
        </Pressable>
      </View>

      {detailQuery.isError ? (
        <ErrorState
          title="Could not load entry"
          message={detailQuery.error.message}
          onRetry={() => {
            void detailQuery.refetch();
          }}
        />
      ) : null}

      {(createMutation.isError || updateMutation.isError) ? (
        <ErrorState
          title="Could not save entry"
          message={createMutation.error?.message ?? updateMutation.error?.message ?? "Please try again."}
          onRetry={() => {
            void handleSave();
          }}
        />
      ) : null}

      <View style={styles.modeRow}>
        <Pressable onPress={() => setMode("quick")} style={[styles.modeButton, mode === "quick" ? styles.modeButtonActive : null]}>
          <Text style={[styles.modeLabel, mode === "quick" ? styles.modeLabelActive : null]}>Quick add</Text>
        </Pressable>
        <Pressable onPress={() => setMode("full")} style={[styles.modeButton, mode === "full" ? styles.modeButtonActive : null]}>
          <Text style={[styles.modeLabel, mode === "full" ? styles.modeLabelActive : null]}>Full entry</Text>
        </Pressable>
      </View>

      <TextField
        label="What was it?"
        value={title}
        onChangeText={setTitle}
        placeholder="Groceries, utility bill, lunch..."
        error={errors.title}
      />

      <TextField
        label={`Amount (${currency})`}
        value={amount}
        onChangeText={setAmount}
        placeholder="0.00"
        keyboardType="decimal-pad"
        error={errors.amount}
      />

      <SelectField
        label="Category"
        value={category}
        options={categoryOptions}
        onChange={setCategory}
        error={errors.category}
        compact
      />

      {mode === "full" ? (
        <>
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

          <SelectField
            label="Payment source"
            value={paymentSource}
            options={paymentSourceOptions}
            onChange={setPaymentSource}
            error={errors.paymentSource}
            compact
          />

          <SelectField
            label="Status"
            value={status}
            options={expenseTrackerStatusOptions.map((option) => ({ label: option.label, value: option.value }))}
            onChange={(value) => setStatus(value as "planned" | "completed")}
            compact
          />

          <SelectField
            label="Recurring"
            value={isRecurring ? "yes" : "no"}
            options={[
              { label: "No", value: "no" },
              { label: "Yes", value: "yes" }
            ]}
            onChange={(value) => setIsRecurring(value === "yes")}
            compact
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
            placeholder="Optional note"
            multiline
            style={styles.notesField}
          />

          <TextField
            label="Tags"
            value={tagsText}
            onChangeText={setTagsText}
            placeholder="home, family, recurring"
          />
        </>
      ) : (
        <Pressable style={styles.expandButton} onPress={() => setMode("full")}>
          <Text style={styles.expandLabel}>Add date, source, notes, tags, and planning details</Text>
        </Pressable>
      )}

      <View style={styles.actionsRow}>
        <Pressable onPress={() => void handleSave()} style={[styles.primaryButton, (createMutation.isPending || updateMutation.isPending) ? styles.buttonDisabled : null]} disabled={createMutation.isPending || updateMutation.isPending}>
          <Text style={styles.primaryButtonLabel}>{isEditing ? "Save changes" : "Save expense"}</Text>
        </Pressable>
        <Pressable onPress={() => router.back()} style={styles.secondaryButton}>
          <Text style={styles.secondaryButtonLabel}>Cancel</Text>
        </Pressable>
      </View>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[24],
    gap: spacing[16]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  subtitle: {
    marginTop: spacing[4],
    color: palette.textSecondary,
    ...typography.body2,
    maxWidth: 280
  },
  closeButton: {
    width: 40,
    height: 40,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    alignItems: "center",
    justifyContent: "center"
  },
  modeRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  modeButton: {
    flex: 1,
    minHeight: 42,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)",
    alignItems: "center",
    justifyContent: "center"
  },
  modeButtonActive: {
    borderColor: "rgba(99,165,255,0.54)",
    backgroundColor: "rgba(47,107,255,0.26)"
  },
  modeLabel: {
    color: palette.textSecondary,
    ...typography.body2
  },
  modeLabelActive: {
    color: palette.textPrimary,
    fontWeight: "700"
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
  expandButton: {
    minHeight: 46,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.76)",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  expandLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  actionsRow: {
    gap: spacing[12],
    marginTop: spacing[4],
    paddingBottom: spacing[24]
  },
  primaryButton: {
    minHeight: 48,
    borderRadius: 16,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(47,107,255,0.9)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.4)"
  },
  buttonDisabled: {
    opacity: 0.6
  },
  primaryButtonLabel: {
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700"
  },
  secondaryButton: {
    minHeight: 46,
    borderRadius: 16,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.72)"
  },
  secondaryButtonLabel: {
    color: palette.textSecondary,
    ...typography.body2
  }
});
