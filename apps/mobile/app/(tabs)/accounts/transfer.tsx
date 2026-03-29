import { Ionicons } from "@expo/vector-icons";
import { Redirect, useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Modal, Pressable, ScrollView, Text, View } from "react-native";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import { useAccountsQuery } from "../../../src/features/accounts/useAccounts";
import { useConnectBankCtaLabels } from "../../../src/features/banking/connectBankCta";
import { HeaderShell } from "../../../src/layout/appHeader";
import { useAuthSession } from "../../../src/providers/AuthProvider";
import { useRuntimeBottomInsetPolicy } from "../../../src/theme/insets";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";

type TransferMode = "external" | "internal";
type DropdownKey = "from-account" | "to-account" | "country" | "currency";

type IbanCountry = {
  code: string;
  name: string;
  placeholder: string;
};

type CurrencyOption = {
  code: string;
  name: string;
};

const ibanCountries: IbanCountry[] = [
  { code: "AT", name: "Austria", placeholder: "AT611904300234573201" },
  { code: "BE", name: "Belgium", placeholder: "BE68539007547034" },
  { code: "DE", name: "Germany", placeholder: "DE89370400440532013000" },
  { code: "ES", name: "Spain", placeholder: "ES9121000418450200051332" },
  { code: "FR", name: "France", placeholder: "FR7630006000011234567890189" },
  { code: "GB", name: "United Kingdom", placeholder: "GB29NWBK60161331926819" },
  { code: "IE", name: "Ireland", placeholder: "IE29AIBK93115212345678" },
  { code: "IT", name: "Italy", placeholder: "IT60X0542811101000000123456" },
  { code: "NL", name: "Netherlands", placeholder: "NL91ABNA0417164300" },
  { code: "PT", name: "Portugal", placeholder: "PT50000201231234567890154" }
];

const transferCurrencies: CurrencyOption[] = [
  { code: "EUR", name: "Euro" },
  { code: "GBP", name: "Pounds Sterling" },
  { code: "USD", name: "US Dollar" },
  { code: "CHF", name: "Swiss Franc" }
];

function formatIbanPlaceholder(rawValue: string) {
  const compact = rawValue.replace(/\s+/g, "");
  return compact.replace(/(.{4})/g, "$1 ").trim();
}

function DropdownField({
  label,
  valueLabel,
  placeholder,
  onPress
}: {
  label: string;
  valueLabel: string;
  placeholder: string;
  onPress: () => void;
}) {
  return (
    <View style={styles.dropdownWrap}>
      <Text style={styles.dropdownLabel}>{label}</Text>
      <Pressable
        style={({ pressed }) => [styles.dropdownButton, pressed ? styles.dropdownButtonPressed : null]}
        onPress={onPress}
      >
        <Text style={styles.dropdownValue} numberOfLines={1}>
          {valueLabel || placeholder}
        </Text>
        <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />
      </Pressable>
    </View>
  );
}

export default function TransferMoneyScreen() {
  const router = useRouter();
  const bottomInsetPolicy = useRuntimeBottomInsetPolicy();
  const params = useLocalSearchParams<{ mode?: string }>();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const accountsQuery = useAccountsQuery();
  const connectBankCta = useConnectBankCtaLabels();
  const [mode, setMode] = useState<TransferMode>(params.mode === "internal" ? "internal" : "external");
  const [activeDropdown, setActiveDropdown] = useState<DropdownKey | null>(null);

  const accountOptions = useMemo(
    () =>
      (accountsQuery.data ?? []).map((account) => ({
        value: account.id,
        label: `${account.name} (${account.currency})`
      })),
    [accountsQuery.data]
  );

  const [fromAccountId, setFromAccountId] = useState("");
  const [toAccountId, setToAccountId] = useState("");
  const [amount, setAmount] = useState("");
  const [reference, setReference] = useState("");
  const [ibanCountryCode, setIbanCountryCode] = useState("IE");
  const [ibanValue, setIbanValue] = useState("");
  const [currencyCode, setCurrencyCode] = useState("EUR");
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");

  useEffect(() => {
    if (!fromAccountId && accountOptions.length > 0) {
      setFromAccountId(accountOptions[0].value);
    }
  }, [accountOptions, fromAccountId]);

  useEffect(() => {
    if (!toAccountId && accountOptions.length > 1) {
      const fallback = accountOptions.find((option) => option.value !== fromAccountId);
      if (fallback) {
        setToAccountId(fallback.value);
      }
    }
  }, [accountOptions, fromAccountId, toAccountId]);

  useEffect(() => {
    if (toAccountId && toAccountId === fromAccountId) {
      const fallback = accountOptions.find((option) => option.value !== fromAccountId);
      setToAccountId(fallback?.value ?? "");
    }
  }, [accountOptions, fromAccountId, toAccountId]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const selectedCountry =
    ibanCountries.find((country) => country.code === ibanCountryCode) ?? ibanCountries[0];
  const selectedCurrency =
    transferCurrencies.find((item) => item.code === currencyCode) ?? transferCurrencies[0];
  const fromAccountLabel =
    accountOptions.find((option) => option.value === fromAccountId)?.label ?? "";
  const toAccountLabel =
    accountOptions.find((option) => option.value === toAccountId)?.label ?? "";

  const dropdownTitle =
    activeDropdown === "from-account"
      ? "Select source account"
      : activeDropdown === "to-account"
        ? "Select destination account"
        : activeDropdown === "country"
          ? "Select country"
          : activeDropdown === "currency"
            ? "Select currency"
            : "";

  const dropdownOptions =
    activeDropdown === "from-account"
      ? accountOptions
      : activeDropdown === "to-account"
        ? accountOptions.filter((option) => option.value !== fromAccountId)
        : activeDropdown === "country"
          ? ibanCountries.map((country) => ({
              value: country.code,
              label: `${country.code} - ${country.name}`
            }))
          : activeDropdown === "currency"
            ? transferCurrencies.map((item) => ({
                value: item.code,
                label: `${item.code} - ${item.name}`
              }))
            : [];

  const selectedDropdownValue =
    activeDropdown === "from-account"
      ? fromAccountId
      : activeDropdown === "to-account"
        ? toAccountId
        : activeDropdown === "country"
          ? ibanCountryCode
          : activeDropdown === "currency"
            ? currencyCode
            : "";

  const canContinue =
    mode === "internal"
      ? Boolean(fromAccountId) &&
        Boolean(toAccountId) &&
        fromAccountId !== toAccountId &&
        Number(amount) > 0
      : Boolean(fromAccountId) &&
        Boolean(ibanValue.trim()) &&
        Boolean(firstName.trim()) &&
        Boolean(lastName.trim()) &&
        Number(amount) > 0;

  return (
    <ScreenContainer contentStyle={styles.content} scrollable={false}>
      <HeaderShell preset="secondaryDetail" title="Transfer Money" />

      {(accountsQuery.data?.length ?? 0) === 0 ? (
        <EmptyState
          title="No connected accounts"
          message="Connect your bank first to set a source account."
          actionLabel={connectBankCta.primaryLabel}
          onActionPress={() => router.push("/(tabs)/accounts/connect-bank?intent=new")}
          hideOrb
          centerText
        />
      ) : (
        <>
          <View style={styles.modeRail}>
            <Pressable
              style={[styles.modeChip, mode === "external" ? styles.modeChipActive : null]}
              onPress={() => setMode("external")}
            >
              <Text style={[styles.modeChipLabel, mode === "external" ? styles.modeChipLabelActive : null]}>
                Bank transfer
              </Text>
            </Pressable>
            <Pressable
              style={[styles.modeChip, mode === "internal" ? styles.modeChipActive : null]}
              onPress={() => setMode("internal")}
            >
              <Text style={[styles.modeChipLabel, mode === "internal" ? styles.modeChipLabelActive : null]}>
                Between my accounts
              </Text>
            </Pressable>
          </View>

          <ScrollView
            contentContainerStyle={styles.formWrap}
            showsVerticalScrollIndicator={false}
            keyboardShouldPersistTaps="handled"
          >
            <DropdownField
              label="From account"
              valueLabel={fromAccountLabel}
              placeholder="Select account"
              onPress={() => setActiveDropdown("from-account")}
            />

            {mode === "internal" ? (
              <>
                <DropdownField
                  label="To account"
                  valueLabel={toAccountLabel}
                  placeholder="Select destination account"
                  onPress={() => setActiveDropdown("to-account")}
                />
                <TextField
                  label="Amount"
                  value={amount}
                  onChangeText={setAmount}
                  placeholder="0.00"
                  keyboardType="decimal-pad"
                />
                <TextField
                  label="Reference (optional)"
                  value={reference}
                  onChangeText={setReference}
                  placeholder="Savings top-up"
                />
                <Text style={styles.helperText}>
                  Use this mode to move money between your own connected accounts.
                </Text>
              </>
            ) : (
              <>
                <View style={styles.ibanRow}>
                  <View style={styles.countrySelector}>
                    <DropdownField
                      label="Country"
                      valueLabel={selectedCountry.code}
                      placeholder="Country"
                      onPress={() => setActiveDropdown("country")}
                    />
                  </View>
                  <View style={styles.ibanInput}>
                    <TextField
                      label="IBAN"
                      value={ibanValue}
                      onChangeText={setIbanValue}
                      placeholder={formatIbanPlaceholder(selectedCountry.placeholder)}
                      autoCapitalize="characters"
                      autoCorrect={false}
                    />
                  </View>
                </View>

                <DropdownField
                  label="Currency"
                  valueLabel={`${selectedCurrency.code} - ${selectedCurrency.name}`}
                  placeholder="Select currency"
                  onPress={() => setActiveDropdown("currency")}
                />

                <TextField
                  label="Amount"
                  value={amount}
                  onChangeText={setAmount}
                  placeholder="0.00"
                  keyboardType="decimal-pad"
                />

                <View style={styles.nameRow}>
                  <View style={styles.nameField}>
                    <TextField
                      label="First name"
                      value={firstName}
                      onChangeText={setFirstName}
                      placeholder="Alex"
                    />
                  </View>
                  <View style={styles.nameField}>
                    <TextField
                      label="Last name"
                      value={lastName}
                      onChangeText={setLastName}
                      placeholder="Smith"
                    />
                  </View>
                </View>

                <TextField
                  label="Email (optional)"
                  value={email}
                  onChangeText={setEmail}
                  keyboardType="email-address"
                  autoCapitalize="none"
                  placeholder="alex@email.com"
                />

                <TextField
                  label="Reference (optional)"
                  value={reference}
                  onChangeText={setReference}
                  placeholder="Rent March"
                />

                <Text style={styles.helperText}>
                  External transfers use a beneficiary account and review flow instead of a separate send-money page.
                </Text>
              </>
            )}
          </ScrollView>
        </>
      )}

      <Modal
        visible={activeDropdown !== null}
        transparent
        animationType="fade"
        onRequestClose={() => setActiveDropdown(null)}
      >
        <Pressable style={styles.modalOverlay} onPress={() => setActiveDropdown(null)}>
          <Pressable
            style={[
              styles.modalSheet,
              { paddingBottom: spacing[12] + bottomInsetPolicy.bottomActionInsetTight }
            ]}
            onPress={() => undefined}
          >
            <Text style={styles.modalTitle}>{dropdownTitle}</Text>
            <ScrollView
              contentContainerStyle={[
                styles.modalList,
                { paddingBottom: spacing[4] + bottomInsetPolicy.bottomScrollableInset }
              ]}
              showsVerticalScrollIndicator={false}
            >
              {dropdownOptions.map((option) => (
                <Pressable
                  key={option.value}
                  style={({ pressed }) => [
                    styles.modalOption,
                    selectedDropdownValue === option.value ? styles.modalOptionActive : null,
                    pressed ? styles.modalOptionPressed : null
                  ]}
                  onPress={() => {
                    if (activeDropdown === "from-account") {
                      setFromAccountId(option.value);
                    } else if (activeDropdown === "to-account") {
                      setToAccountId(option.value);
                    } else if (activeDropdown === "country") {
                      setIbanCountryCode(option.value);
                    } else if (activeDropdown === "currency") {
                      setCurrencyCode(option.value);
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
        <PrimaryButton label="Review transfer" onPress={() => undefined} disabled={!canContinue} />
        <SecondaryButton label="Cancel" onPress={() => router.back()} />
      </View>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    flex: 1,
    gap: spacing[16]
  },
  modeRail: {
    flexDirection: "row",
    gap: spacing[8]
  },
  modeChip: {
    flex: 1,
    minHeight: 40,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(21,21,21,0.72)",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  modeChipActive: {
    backgroundColor: "rgba(242,140,40,0.22)",
    borderColor: palette.primaryGlow
  },
  modeChipLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "600"
  },
  modeChipLabelActive: {
    color: palette.textPrimary
  },
  formWrap: {
    gap: spacing[12],
    paddingBottom: spacing[20]
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
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: palette.elevatedBackground,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  dropdownButtonPressed: {
    opacity: 0.9
  },
  dropdownValue: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body1
  },
  ibanRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  countrySelector: {
    width: 112
  },
  ibanInput: {
    flex: 1
  },
  nameRow: {
    flexDirection: "row",
    gap: spacing[8]
  },
  nameField: {
    flex: 1
  },
  helperText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: "rgba(9,9,9,0.74)",
    justifyContent: "flex-end"
  },
  modalSheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(17,17,17,0.98)",
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
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(21,21,21,0.74)",
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
    gap: spacing[12]
  }
}));

