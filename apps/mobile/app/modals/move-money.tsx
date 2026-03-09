import { Ionicons } from "@expo/vector-icons";
import { Redirect, useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { Modal, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import { EmptyState } from "../../src/components/ui/EmptyState";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { useAccountsQuery } from "../../src/features/accounts/useAccounts";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";

const fxRates: Record<string, number> = {
  "EUR:GBP": 0.86,
  "GBP:EUR": 1.16,
  "EUR:USD": 1.08,
  "USD:EUR": 0.93,
  "GBP:USD": 1.25,
  "USD:GBP": 0.8
};

function convertAmount(amount: number, fromCurrency: string, toCurrency: string) {
  if (fromCurrency === toCurrency) {
    return amount;
  }

  const directRate = fxRates[`${fromCurrency}:${toCurrency}`];
  if (directRate) {
    return amount * directRate;
  }

  const fallbackViaEur =
    fxRates[`${fromCurrency}:EUR`] && fxRates[`EUR:${toCurrency}`]
      ? amount * fxRates[`${fromCurrency}:EUR`] * fxRates[`EUR:${toCurrency}`]
      : amount;

  return fallbackViaEur;
}

function getCurrencySymbol(currency: string) {
  const formatter = new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency,
    minimumFractionDigits: 0,
    maximumFractionDigits: 0
  });
  const part = formatter.formatToParts(0).find((item) => item.type === "currency");
  return part?.value ?? currency;
}

type DropdownKey = "from" | "to";
type DropdownFieldTone = "from" | "to";

type DropdownFieldProps = {
  label: string;
  valueLabel: string;
  placeholder: string;
  tone: DropdownFieldTone;
  onPress: () => void;
};

function sanitizeAmountInput(raw: string) {
  const normalized = raw.replace(",", ".");
  const cleaned = normalized.replace(/[^0-9.]/g, "");
  const firstDot = cleaned.indexOf(".");
  if (firstDot < 0) {
    return cleaned;
  }

  const whole = cleaned.slice(0, firstDot);
  const fraction = cleaned
    .slice(firstDot + 1)
    .replace(/\./g, "")
    .slice(0, 2);
  return `${whole}.${fraction}`;
}

function parsePositiveAmount(raw: string) {
  if (!raw.trim()) {
    return null;
  }

  const value = Number(raw);
  if (!Number.isFinite(value) || value <= 0) {
    return null;
  }

  return value;
}

function formatAmountInput(value: number) {
  return value.toFixed(2).replace(/\.?0+$/, "");
}

function DropdownField({ label, valueLabel, placeholder, tone, onPress }: DropdownFieldProps) {
  const toneStyles = tone === "from" ? styles.dropdownFrom : styles.dropdownTo;
  const dotToneStyles = tone === "from" ? styles.dropdownDotFrom : styles.dropdownDotTo;

  return (
    <View style={styles.dropdownWrap}>
      <Text style={styles.dropdownLabel}>{label}</Text>
      <Pressable
        style={({ pressed }) => [
          styles.dropdownButton,
          toneStyles,
          pressed ? styles.dropdownButtonPressed : null
        ]}
        onPress={onPress}
      >
        <View style={styles.dropdownValueWrap}>
          <View style={[styles.dropdownDot, dotToneStyles]} />
          <Text style={styles.dropdownValue} numberOfLines={1}>
            {valueLabel || placeholder}
          </Text>
        </View>
        <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />
      </Pressable>
    </View>
  );
}

export default function MoveMoneyModalScreen() {
  const router = useRouter();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const accountsQuery = useAccountsQuery();

  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data]);
  const accountOptions = useMemo(
    () =>
      accounts.map((account) => ({
        value: account.id,
        label: `${account.name} (${account.currency})`
      })),
    [accounts]
  );

  const [fromAccountId, setFromAccountId] = useState("");
  const [toAccountId, setToAccountId] = useState("");
  const [fromAmountInput, setFromAmountInput] = useState("");
  const [toAmountInput, setToAmountInput] = useState("");
  const [activeDropdown, setActiveDropdown] = useState<DropdownKey | null>(null);
  const fromAmountInputRef = useRef<TextInput | null>(null);
  const toAmountInputRef = useRef<TextInput | null>(null);

  useEffect(() => {
    if (accounts.length < 2) {
      return;
    }

    if (!fromAccountId) {
      setFromAccountId(accounts[0].id);
      setToAccountId(accounts[1].id);
      return;
    }

    if (!toAccountId || toAccountId === fromAccountId) {
      const candidate = accounts.find((account) => account.id !== fromAccountId);
      if (candidate) {
        setToAccountId(candidate.id);
      }
    }
  }, [accounts, fromAccountId, toAccountId]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const fromAccount = accounts.find((account) => account.id === fromAccountId) ?? null;
  const toAccount = accounts.find((account) => account.id === toAccountId) ?? null;
  const hasTypedAmount = fromAmountInput.trim().length > 0 || toAmountInput.trim().length > 0;
  const fromSymbol = fromAccount ? getCurrencySymbol(fromAccount.currency) : "€";
  const toSymbol = toAccount ? getCurrencySymbol(toAccount.currency) : "€";

  const fromParsedAmount = parsePositiveAmount(fromAmountInput);
  const toParsedAmount = parsePositiveAmount(toAmountInput);

  const canContinue =
    Boolean(fromAccount) &&
    Boolean(toAccount) &&
    fromAccount?.id !== toAccount?.id &&
    ((fromParsedAmount ?? 0) > 0 || (toParsedAmount ?? 0) > 0);
  const fromAccountLabel =
    accountOptions.find((option) => option.value === fromAccountId)?.label ?? "";
  const toAccountLabel =
    accountOptions.find((option) => option.value === toAccountId)?.label ?? "";
  const dropdownTitle = activeDropdown === "from" ? "Select source account" : "Select destination account";
  const dropdownOptions =
    activeDropdown === "from"
      ? accountOptions
      : accountOptions.filter((option) => option.value !== fromAccountId);
  const selectedDropdownValue = activeDropdown === "from" ? fromAccountId : toAccountId;
  const handleFromAmountChange = (rawValue: string) => {
    const sanitized = sanitizeAmountInput(rawValue);
    setFromAmountInput(sanitized);

    if (!fromAccount || !toAccount) {
      setToAmountInput("");
      return;
    }

    const amount = parsePositiveAmount(sanitized);
    if (amount === null) {
      setToAmountInput("");
      return;
    }

    const converted = convertAmount(amount, fromAccount.currency, toAccount.currency);
    setToAmountInput(formatAmountInput(converted));
  };

  const handleToAmountChange = (rawValue: string) => {
    const sanitized = sanitizeAmountInput(rawValue);
    setToAmountInput(sanitized);

    if (!fromAccount || !toAccount) {
      setFromAmountInput("");
      return;
    }

    const amount = parsePositiveAmount(sanitized);
    if (amount === null) {
      setFromAmountInput("");
      return;
    }

    const converted = convertAmount(amount, toAccount.currency, fromAccount.currency);
    setFromAmountInput(formatAmountInput(converted));
  };

  const handleSwapAccounts = () => {
    if (!fromAccountId || !toAccountId) {
      return;
    }

    setFromAccountId(toAccountId);
    setToAccountId(fromAccountId);
    setFromAmountInput("");
    setToAmountInput("");
  };

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.header}>
        <Text style={styles.title}>Move money</Text>
      </View>

      {accounts.length < 2 ? (
        <EmptyState
          title="Need at least two accounts"
          message="Connect another account to move money between your own accounts."
          actionLabel="Connect bank"
          onActionPress={() => router.push("/modals/add-account")}
        />
      ) : (
        <View style={styles.formWrap}>
          <View style={styles.accountSelectorsWrap}>
            <DropdownField
              label="From account"
              valueLabel={fromAccountLabel}
              placeholder="Select source account"
              tone="from"
              onPress={() => setActiveDropdown("from")}
            />
            <DropdownField
              label="To account"
              valueLabel={toAccountLabel}
              placeholder="Select destination account"
              tone="to"
              onPress={() => setActiveDropdown("to")}
            />
            <Pressable
              style={({ pressed }) => [styles.swapButton, pressed ? styles.swapButtonPressed : null]}
              onPress={handleSwapAccounts}
            >
              <Ionicons name="swap-vertical" size={16} color={palette.textPrimary} />
            </Pressable>
          </View>

          <View style={styles.splitAmountCard}>
            <View style={styles.splitAmountRow}>
              <View style={styles.leftAmountInputWrap}>
                <Text style={[styles.leftAmountPrefix, hasTypedAmount ? styles.leftAmountActive : styles.placeholderTone]}>
                  -{fromSymbol}
                </Text>
                <TextInput
                  ref={fromAmountInputRef}
                  value={fromAmountInput}
                  onChangeText={handleFromAmountChange}
                  keyboardType="decimal-pad"
                  placeholder="0"
                  placeholderTextColor={palette.textSecondary}
                  style={[styles.leftAmountInput, hasTypedAmount ? styles.leftAmountActive : styles.placeholderTone]}
                  selectionColor={palette.textPrimary}
                />
              </View>
              <View style={styles.divider} />
              <View style={styles.rightAmountInputWrap}>
                <Text style={[styles.rightAmountPrefix, hasTypedAmount ? styles.rightAmountActive : styles.placeholderTone]}>
                  +{toSymbol}
                </Text>
                <TextInput
                  ref={toAmountInputRef}
                  value={toAmountInput}
                  onChangeText={handleToAmountChange}
                  keyboardType="decimal-pad"
                  placeholder="0"
                  placeholderTextColor={palette.textSecondary}
                  style={[styles.rightAmountInput, hasTypedAmount ? styles.rightAmountActive : styles.placeholderTone]}
                  selectionColor={palette.textPrimary}
                />
              </View>
            </View>
            {fromAccount && toAccount && fromAccount.currency !== toAccount.currency ? (
              <Text style={styles.fxHint}>Estimated conversion shown for now.</Text>
            ) : null}
          </View>
        </View>
      )}

      <Modal
        visible={activeDropdown !== null}
        transparent
        animationType="fade"
        onRequestClose={() => setActiveDropdown(null)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setActiveDropdown(null)}>
          <Pressable style={styles.modalSheet} onPress={() => undefined}>
            <Text style={styles.modalTitle}>{dropdownTitle}</Text>
            <ScrollView contentContainerStyle={styles.modalList} showsVerticalScrollIndicator={false}>
              {dropdownOptions.map((option) => (
                <Pressable
                  key={option.value}
                  style={({ pressed }) => [
                    styles.modalOption,
                    option.value === selectedDropdownValue ? styles.modalOptionActive : null,
                    pressed ? styles.modalOptionPressed : null
                  ]}
                  onPress={() => {
                    if (activeDropdown === "from") {
                      setFromAccountId(option.value);
                      if (option.value === toAccountId) {
                        const candidate = accounts.find((account) => account.id !== option.value);
                        if (candidate) {
                          setToAccountId(candidate.id);
                        }
                      }
                    } else if (activeDropdown === "to") {
                      setToAccountId(option.value);
                    }

                    setActiveDropdown(null);
                  }}
                >
                  <Text style={styles.modalOptionLabel}>{option.label}</Text>
                </Pressable>
              ))}
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>

      <View style={styles.actions}>
        <PrimaryButton label="Move" onPress={() => undefined} disabled={!canContinue} />
        <SecondaryButton label="Cancel" onPress={() => router.back()} />
      </View>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    flex: 1,
    paddingTop: spacing[20],
    gap: spacing[16]
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  formWrap: {
    gap: spacing[12]
  },
  accountSelectorsWrap: {
    position: "relative",
    gap: spacing[12],
    paddingRight: 44
  },
  dropdownWrap: {
    gap: spacing[8]
  },
  dropdownLabel: {
    color: palette.textPrimary,
    ...typography.caption
  },
  dropdownButton: {
    minHeight: 50,
    borderRadius: 12,
    borderWidth: 1,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  dropdownFrom: {
    borderColor: "rgba(244,104,119,0.45)",
    backgroundColor: "rgba(90,16,30,0.24)"
  },
  dropdownTo: {
    borderColor: "rgba(28,197,131,0.45)",
    backgroundColor: "rgba(9,61,41,0.26)"
  },
  dropdownButtonPressed: {
    opacity: 0.92
  },
  dropdownValueWrap: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  dropdownDot: {
    width: 8,
    height: 8,
    borderRadius: 4
  },
  dropdownDotFrom: {
    backgroundColor: palette.negative
  },
  dropdownDotTo: {
    backgroundColor: palette.success
  },
  dropdownValue: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body1
  },
  swapButton: {
    position: "absolute",
    right: 0,
    top: 74,
    width: 34,
    height: 34,
    borderRadius: 17,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(23,45,74,0.9)",
    alignItems: "center",
    justifyContent: "center"
  },
  swapButtonPressed: {
    opacity: 0.9
  },
  splitAmountCard: {
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(16,34,55,0.78)",
    padding: spacing[12],
    gap: spacing[12]
  },
  splitAmountRow: {
    minHeight: 56,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "rgba(220,232,255,0.16)",
    backgroundColor: "rgba(8,18,30,0.66)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: spacing[12]
  },
  leftAmountInputWrap: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    minHeight: 44
  },
  leftAmountPrefix: {
    ...typography.title2
  },
  leftAmountInput: {
    flex: 1,
    minWidth: 30,
    padding: 0,
    margin: 0,
    backgroundColor: "transparent",
    ...typography.title2
  },
  leftAmountActive: {
    color: palette.negative
  },
  rightAmountActive: {
    color: palette.success
  },
  placeholderTone: {
    color: palette.textSecondary
  },
  rightAmountInputWrap: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    minHeight: 44,
    marginLeft: spacing[8]
  },
  rightAmountPrefix: {
    ...typography.title2
  },
  rightAmountInput: {
    minWidth: 30,
    maxWidth: "70%",
    padding: 0,
    margin: 0,
    backgroundColor: "transparent",
    textAlign: "left",
    ...typography.title2
  },
  divider: {
    width: 1,
    height: 24,
    backgroundColor: "rgba(220,232,255,0.24)"
  },
  fxHint: {
    color: palette.textSecondary,
    ...typography.caption
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: "rgba(4,11,23,0.74)",
    justifyContent: "flex-end"
  },
  modalSheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    padding: spacing[16],
    gap: spacing[12],
    maxHeight: "82%"
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  modalList: {
    gap: spacing[8],
    paddingBottom: spacing[8]
  },
  modalOption: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    minHeight: 46,
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  modalOptionActive: {
    borderColor: palette.primaryGlow
  },
  modalOptionPressed: {
    opacity: 0.9
  },
  modalOptionLabel: {
    color: palette.textPrimary,
    ...typography.body2
  },
  actions: {
    gap: spacing[12],
    marginTop: "auto"
  }
});
