import { Ionicons } from "@expo/vector-icons";
import { Redirect, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Modal, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { EmptyState } from "../../src/components/ui/EmptyState";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useAccountsQuery } from "../../src/features/accounts/useAccounts";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";

type IbanCountry = {
  code: string;
  name: string;
  flag: string;
  placeholder: string;
};

type CurrencyOption = {
  code: string;
  name: string;
  flag: string;
};

function formatIbanPlaceholder(rawValue: string) {
  const compact = rawValue.replace(/\s+/g, "");
  return compact.replace(/(.{4})/g, "$1 ").trim();
}

const ibanCountries: IbanCountry[] = [
  { code: "AT", name: "Austria", flag: "🇦🇹", placeholder: "AT611904300234573201" },
  { code: "BE", name: "Belgium", flag: "🇧🇪", placeholder: "BE68539007547034" },
  { code: "BG", name: "Bulgaria", flag: "🇧🇬", placeholder: "BG80BNBG96611020345678" },
  { code: "HR", name: "Croatia", flag: "🇭🇷", placeholder: "HR1210010051863000160" },
  { code: "CY", name: "Cyprus", flag: "🇨🇾", placeholder: "CY17002001280000001200527600" },
  { code: "CZ", name: "Czech Republic", flag: "🇨🇿", placeholder: "CZ6508000000192000145399" },
  { code: "DK", name: "Denmark", flag: "🇩🇰", placeholder: "DK5000400440116243" },
  { code: "EE", name: "Estonia", flag: "🇪🇪", placeholder: "EE382200221020145685" },
  { code: "FI", name: "Finland", flag: "🇫🇮", placeholder: "FI2112345600000785" },
  { code: "FR", name: "France", flag: "🇫🇷", placeholder: "FR7630006000011234567890189" },
  { code: "DE", name: "Germany", flag: "🇩🇪", placeholder: "DE89370400440532013000" },
  { code: "GR", name: "Greece", flag: "🇬🇷", placeholder: "GR1601101250000000012300695" },
  { code: "HU", name: "Hungary", flag: "🇭🇺", placeholder: "HU42117730161111101800000000" },
  { code: "IE", name: "Ireland", flag: "🇮🇪", placeholder: "IE29AIBK93115212345678" },
  { code: "IT", name: "Italy", flag: "🇮🇹", placeholder: "IT60X0542811101000000123456" },
  { code: "LV", name: "Latvia", flag: "🇱🇻", placeholder: "LV80BANK0000435195001" },
  { code: "LT", name: "Lithuania", flag: "🇱🇹", placeholder: "LT121000011101001000" },
  { code: "LU", name: "Luxembourg", flag: "🇱🇺", placeholder: "LU280019400644750000" },
  { code: "MT", name: "Malta", flag: "🇲🇹", placeholder: "MT84MALT011000012345MTLCAST001S" },
  { code: "NL", name: "Netherlands", flag: "🇳🇱", placeholder: "NL91ABNA0417164300" },
  { code: "PL", name: "Poland", flag: "🇵🇱", placeholder: "PL61109010140000071219812874" },
  { code: "PT", name: "Portugal", flag: "🇵🇹", placeholder: "PT50000201231234567890154" },
  { code: "RO", name: "Romania", flag: "🇷🇴", placeholder: "RO49AAAA1B31007593840000" },
  { code: "SK", name: "Slovakia", flag: "🇸🇰", placeholder: "SK3112000000198742637541" },
  { code: "SI", name: "Slovenia", flag: "🇸🇮", placeholder: "SI56192001234567892" },
  { code: "ES", name: "Spain", flag: "🇪🇸", placeholder: "ES9121000418450200051332" },
  { code: "SE", name: "Sweden", flag: "🇸🇪", placeholder: "SE4550000000058398257466" },
  { code: "GB", name: "United Kingdom", flag: "🇬🇧", placeholder: "GB29NWBK60161331926819" }
];

const transferCurrencies: CurrencyOption[] = [
  { code: "EUR", name: "Euro", flag: "🇪🇺" },
  { code: "BGN", name: "Bulgarian Lev", flag: "🇧🇬" },
  { code: "CZK", name: "Czech Koruna", flag: "🇨🇿" },
  { code: "DKK", name: "Danish Krone", flag: "🇩🇰" },
  { code: "HUF", name: "Hungarian Forint", flag: "🇭🇺" },
  { code: "PLN", name: "Polish Zloty", flag: "🇵🇱" },
  { code: "RON", name: "Romanian Leu", flag: "🇷🇴" },
  { code: "SEK", name: "Swedish Krona", flag: "🇸🇪" },
  { code: "NOK", name: "Norwegian Krone", flag: "🇳🇴" },
  { code: "ISK", name: "Icelandic Krona", flag: "🇮🇸" },
  { code: "CHF", name: "Swiss Franc", flag: "🇨🇭" },
  { code: "GBP", name: "Pounds", flag: "🇬🇧" },
  { code: "USD", name: "US Dollar", flag: "🇺🇸" },
  { code: "AUD", name: "Australian Dollar", flag: "🇦🇺" }
];

type DropdownKey = "account" | "country" | "currency";

type DropdownFieldProps = {
  label: string;
  valueLabel: string;
  placeholder: string;
  onPress: () => void;
};

function DropdownField({ label, valueLabel, placeholder, onPress }: DropdownFieldProps) {
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

export default function SendMoneyModalScreen() {
  const router = useRouter();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const accountsQuery = useAccountsQuery();

  const accountOptions = useMemo(
    () =>
      (accountsQuery.data ?? []).map((account) => ({
        value: account.id,
        label: `${account.name} (${account.currency})`
      })),
    [accountsQuery.data]
  );

  const [fromAccountId, setFromAccountId] = useState("");
  const [ibanCountryCode, setIbanCountryCode] = useState("IE");
  const [ibanValue, setIbanValue] = useState("");
  const [currencyCode, setCurrencyCode] = useState("EUR");
  const [firstName, setFirstName] = useState("");
  const [middleName, setMiddleName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [activeDropdown, setActiveDropdown] = useState<DropdownKey | null>(null);

  useEffect(() => {
    if (!fromAccountId && accountOptions.length > 0) {
      setFromAccountId(accountOptions[0].value);
    }
  }, [accountOptions, fromAccountId]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const selectedCountry =
    ibanCountries.find((country) => country.code === ibanCountryCode) ?? ibanCountries[0];
  const formattedIbanPlaceholder = formatIbanPlaceholder(selectedCountry.placeholder);
  const selectedCountryLabel = `${selectedCountry.flag} ${selectedCountry.code}`;
  const selectedAccountLabel =
    accountOptions.find((option) => option.value === fromAccountId)?.label ?? "";
  const selectedCurrency =
    transferCurrencies.find((item) => item.code === currencyCode) ?? transferCurrencies[0];
  const selectedCurrencyLabel = `${selectedCurrency.flag} ${selectedCurrency.code} - ${selectedCurrency.name}`;

  const canContinue =
    Boolean(fromAccountId) &&
    Boolean(ibanValue.trim()) &&
    Boolean(firstName.trim()) &&
    Boolean(lastName.trim());
  const placeholderNeedsCompactFont = formattedIbanPlaceholder.length >= 31;
  const placeholderNeedsXCompactFont = formattedIbanPlaceholder.length >= 35;

  const dropdownTitle =
    activeDropdown === "account"
      ? "Select account"
      : activeDropdown === "country"
        ? "Select country"
        : activeDropdown === "currency"
          ? "Select currency"
          : "";
  const dropdownOptions =
    activeDropdown === "account"
      ? accountOptions
      : activeDropdown === "country"
        ? ibanCountries.map((country) => ({
            value: country.code,
            label: `${country.flag} ${country.code} - ${country.name}`
          }))
        : activeDropdown === "currency"
          ? transferCurrencies.map((item) => ({
              value: item.code,
              label: `${item.flag} ${item.code} - ${item.name}`
            }))
          : [];
  const selectedDropdownValue =
    activeDropdown === "account"
      ? fromAccountId
      : activeDropdown === "country"
        ? ibanCountryCode
        : activeDropdown === "currency"
          ? currencyCode
          : "";

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.header}>
        <Text style={styles.title}>Send money</Text>
      </View>

      {(accountsQuery.data?.length ?? 0) === 0 ? (
        <EmptyState
          title="No connected accounts"
          message="Connect your bank first to set a source account."
          actionLabel="Connect bank"
          onActionPress={() => router.push("/modals/add-account")}
        />
      ) : (
        <ScrollView
          contentContainerStyle={styles.formWrap}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          <DropdownField
            label="From account"
            valueLabel={selectedAccountLabel}
            placeholder="Select account"
            onPress={() => setActiveDropdown("account")}
          />

          <View style={styles.ibanRow}>
            <View style={styles.countrySelector}>
              <DropdownField
                label="Country"
                valueLabel={selectedCountryLabel}
                placeholder="Country"
                onPress={() => setActiveDropdown("country")}
              />
            </View>
            <View style={styles.ibanInput}>
              <TextField
                label="IBAN"
                value={ibanValue}
                onChangeText={setIbanValue}
                placeholder={formattedIbanPlaceholder}
                autoCapitalize="characters"
                autoCorrect={false}
                allowFontScaling={false}
                style={[
                  styles.ibanTextInput,
                  !ibanValue && placeholderNeedsXCompactFont ? styles.ibanTextInputXCompact : null,
                  !ibanValue && placeholderNeedsCompactFont ? styles.ibanTextInputCompact : null
                ]}
              />
            </View>
          </View>

          <DropdownField
            label="Currency"
            valueLabel={selectedCurrencyLabel}
            placeholder="Select currency"
            onPress={() => setActiveDropdown("currency")}
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
                label="Middle name (optional)"
                value={middleName}
                onChangeText={setMiddleName}
                placeholder="James"
              />
            </View>
          </View>
          <TextField
            label="Last name"
            value={lastName}
            onChangeText={setLastName}
            placeholder="Smith"
          />
          <TextField
            label="Email (optional)"
            value={email}
            onChangeText={setEmail}
            keyboardType="email-address"
            autoCapitalize="none"
            placeholder="alex@email.com"
          />
          <Text style={styles.helperText}>Email will be used for payment confirmation.</Text>
        </ScrollView>
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
                    selectedDropdownValue === option.value ? styles.modalOptionActive : null,
                    pressed ? styles.modalOptionPressed : null
                  ]}
                  onPress={() => {
                    if (activeDropdown === "account") {
                      setFromAccountId(option.value);
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
        <PrimaryButton
          label="Review transfer"
          onPress={() => undefined}
          disabled={!canContinue}
        />
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
    borderRadius: 12,
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
    gap: 2
  },
  countrySelector: {
    width: 92
  },
  ibanInput: {
    flex: 1
  },
  ibanTextInput: {
    fontSize: 13,
    lineHeight: 18,
    letterSpacing: 0.2
  },
  ibanTextInputCompact: {
    fontSize: 11.5,
    lineHeight: 16
  },
  ibanTextInputXCompact: {
    fontSize: 10,
    lineHeight: 14,
    letterSpacing: 0
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
    gap: spacing[12]
  }
});
