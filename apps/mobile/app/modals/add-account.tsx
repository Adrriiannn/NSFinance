import { Ionicons } from "@expo/vector-icons";
import { Redirect, useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { SelectField } from "../../src/components/ui/SelectField";
import { TextField } from "../../src/components/ui/TextField";
import { useCreateAccountMutation } from "../../src/features/accounts/useAccounts";
import { formatUnknownError } from "../../src/lib/api/errors";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";
import type { AccountType } from "../../src/types/api";

type FormErrors = Partial<Record<"name" | "type" | "currency" | "openingBalance", string>>;

const accountTypeOptions: { label: string; value: AccountType }[] = [
  { label: "Current", value: "Current" },
  { label: "Savings", value: "Savings" },
  { label: "Credit", value: "Credit" },
  { label: "Cash", value: "Cash" },
  { label: "Other", value: "Other" }
];

const currencyOptions = [
  { label: "EUR", value: "EUR" },
  { label: "GBP", value: "GBP" },
  { label: "USD", value: "USD" }
];

export default function AddAccountModalScreen() {
  const router = useRouter();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const { playSuccess } = useFeedbackSound();
  const mutation = useCreateAccountMutation();
  const [name, setName] = useState("");
  const [type, setType] = useState<AccountType>("Current");
  const [currency, setCurrency] = useState("EUR");
  const [openingBalance, setOpeningBalance] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});

  const parsedOpeningBalance = useMemo(() => {
    if (!openingBalance.trim()) {
      return null;
    }

    const parsed = Number(openingBalance);
    return Number.isFinite(parsed) ? parsed : Number.NaN;
  }, [openingBalance]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const validate = () => {
    const nextErrors: FormErrors = {};

    if (!name.trim()) {
      nextErrors.name = "Account name is required.";
    }

    if (!currency || currency.length !== 3) {
      nextErrors.currency = "Currency must be a 3-letter code.";
    }

    if (Number.isNaN(parsedOpeningBalance)) {
      nextErrors.openingBalance = "Opening balance must be numeric.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleSubmit = async () => {
    if (!validate()) {
      return;
    }

    await mutation.mutateAsync({
      name: name.trim(),
      type,
      currency: currency.trim().toUpperCase(),
      openingBalance:
        parsedOpeningBalance === null ? null : Number(parsedOpeningBalance.toFixed(2))
    });

    playSuccess();
    router.back();
  };

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.header}>
        <View>
          <Text style={styles.title}>Add account</Text>
        </View>
        <Ionicons
          name="close"
          size={26}
          color={palette.textSecondary}
          onPress={() => router.back()}
        />
      </View>

      {mutation.isError ? (
        <ErrorState
          title="Could not create account"
          message={formatUnknownError(mutation.error)}
          onRetry={handleSubmit}
          retryLabel="Try again"
        />
      ) : null}

      <TextField
        label="Account name"
        value={name}
        onChangeText={setName}
        placeholder="Main Current"
        autoFocus
        error={errors.name}
      />

      <SelectField
        label="Account type"
        value={type}
        options={accountTypeOptions}
        onChange={(value) => setType(value as AccountType)}
        error={errors.type}
      />

      <SelectField
        label="Currency"
        value={currency}
        options={currencyOptions}
        onChange={setCurrency}
        error={errors.currency}
      />

      <TextField
        label="Opening balance (optional)"
        value={openingBalance}
        onChangeText={setOpeningBalance}
        placeholder="0.00"
        keyboardType="decimal-pad"
        error={errors.openingBalance}
      />

      <View style={styles.actions}>
        <SecondaryButton label="Cancel" onPress={() => router.back()} />
        <PrimaryButton
          label="Save account"
          onPress={() => void handleSubmit()}
          isLoading={mutation.isPending}
        />
      </View>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[20],
    gap: spacing[16]
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center"
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  actions: {
    marginTop: spacing[8],
    gap: spacing[12]
  }
});



