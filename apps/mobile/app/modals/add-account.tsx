import { Ionicons } from "@expo/vector-icons";
import { Redirect, useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import {
  ConnectionStatusIndicator,
  type ConnectionStatus
} from "../../src/components/ui/ConnectionStatusIndicator";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useCreateAccountMutation } from "../../src/features/accounts/useAccounts";
import { formatUnknownError } from "../../src/lib/api/errors";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";
import type { AccountType } from "../../src/types/api";

type ImportedConnection = {
  name: string;
  type: AccountType;
  currency: string;
  balance: number;
};

const mockConnectionProfiles: ImportedConnection[] = [
  {
    name: "Main Current",
    type: "Current",
    currency: "EUR",
    balance: 2643.18
  },
  {
    name: "Everyday Saver",
    type: "Savings",
    currency: "EUR",
    balance: 9080.45
  }
];

export default function AddAccountModalScreen() {
  const router = useRouter();
  const { isAuthenticated, isBootstrapping } = useAuthSession();
  const { playSuccess } = useFeedbackSound();
  const mutation = useCreateAccountMutation();
  const [status, setStatus] = useState<ConnectionStatus>("not_started");
  const [accountName, setAccountName] = useState("");
  const [connectedData, setConnectedData] = useState<ImportedConnection | null>(null);

  const canSave = status === "success" && Boolean(connectedData) && accountName.trim().length > 0;

  const balanceLabel = useMemo(() => {
    if (!connectedData) {
      return "--";
    }

    return connectedData.balance.toFixed(2);
  }, [connectedData]);

  if (!isBootstrapping && !isAuthenticated) {
    return <Redirect href={"/login" as never} />;
  }

  const handleConnectBank = () => {
    setStatus("connecting");

    setTimeout(() => {
      const didSucceed = Math.random() > 0.25;
      if (!didSucceed) {
        setConnectedData(null);
        setStatus("failed");
        return;
      }

      const nextConnection =
        mockConnectionProfiles[Math.floor(Math.random() * mockConnectionProfiles.length)];
      setConnectedData(nextConnection);
      setAccountName(nextConnection.name);
      setStatus("success");
    }, 1200);
  };

  const handleSubmit = async () => {
    if (!connectedData || !canSave) {
      return;
    }

    await mutation.mutateAsync({
      name: accountName.trim(),
      type: connectedData.type,
      currency: connectedData.currency,
      openingBalance: connectedData.balance
    });

    playSuccess();
    router.back();
  };

  return (
    <ScreenContainer contentStyle={styles.content}>
      <View style={styles.header}>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Close"
          onPress={() => router.back()}
          style={({ pressed }) => [styles.closeButton, pressed ? styles.pressed : null]}
        >
          <Ionicons name="arrow-back" size={20} color={palette.textSecondary} />
        </Pressable>
        <Text style={styles.title}>Connect bank account</Text>
      </View>

      <Text style={styles.bodyCopy}>
        Accounts are added through secure bank connection. Sensitive account fields are imported
        from your institution.
      </Text>

      {mutation.isError ? (
        <ErrorState
          title="Could not save account"
          message={formatUnknownError(mutation.error)}
          onRetry={handleSubmit}
          retryLabel="Try again"
        />
      ) : null}

      <ConnectionStatusIndicator status={status} />

      <PrimaryButton
        label={status === "failed" ? "Retry bank connection" : "Connect bank"}
        onPress={handleConnectBank}
        isLoading={status === "connecting"}
      />

      <TextField
        label="Account name"
        value={accountName}
        onChangeText={setAccountName}
        placeholder="Imported from bank"
        editable={status === "success"}
      />

      <TextField
        label="Account type"
        value={connectedData?.type ?? ""}
        onChangeText={() => undefined}
        placeholder="Imported from bank"
        editable={false}
      />

      <TextField
        label="Currency"
        value={connectedData?.currency ?? ""}
        onChangeText={() => undefined}
        placeholder="Imported from bank"
        editable={false}
      />

      <TextField
        label="Balance"
        value={balanceLabel}
        onChangeText={() => undefined}
        placeholder="Imported from bank"
        editable={false}
      />

      <View style={styles.actions}>
        <SecondaryButton label="Cancel" onPress={() => router.back()} />
        <PrimaryButton
          label="Save account"
          onPress={() => void handleSubmit()}
          isLoading={mutation.isPending}
          disabled={!canSave}
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
    alignItems: "center",
    gap: spacing[12]
  },
  closeButton: {
    width: 36,
    height: 36,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    alignItems: "center",
    justifyContent: "center"
  },
  pressed: {
    opacity: 0.85
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  bodyCopy: {
    color: palette.textSecondary,
    ...typography.body2
  },
  actions: {
    marginTop: spacing[8],
    gap: spacing[12]
  }
});

