import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import {
  useCreateDeletionRequestMutation,
  useCreateExportRequestMutation,
  useCreateSupportRequestMutation
} from "../../../src/features/support/useSupport";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { layout, palette, spacing, typography } from "../../../src/theme/tokens";

export default function AccountSupportScreen() {
  const router = useRouter();
  const createSupportMutation = useCreateSupportRequestMutation();
  const createDeletionMutation = useCreateDeletionRequestMutation();
  const createExportMutation = useCreateExportRequestMutation();
  const [category, setCategory] = useState("account_issue");
  const [message, setMessage] = useState("");
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  const submitSupport = async () => {
    const response = await createSupportMutation.mutateAsync({
      category,
      message
    });

    setStatusMessage(`Support request ${response.id} created.`);
    setMessage("");
  };

  return (
    <ScreenContainer contentStyle={styles.content} withBottomTabOffset bottomInsetOffset={spacing[12]}>
      <View style={styles.headerRow}>
        <IconButton
          onPress={() => router.back()}
          icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
        />
        <Text style={styles.headerTitle}>Support</Text>
        <View style={{ width: 42 }} />
      </View>

      {createSupportMutation.isError ? (
        <ErrorState
          title="Support request failed"
          message={formatUnknownError(createSupportMutation.error)}
          onRetry={submitSupport}
          retryLabel="Try again"
        />
      ) : null}

      <GlassCard style={styles.block}>
        <Text style={styles.itemTitle}>Contact support</Text>
        <TextField label="Category" value={category} onChangeText={setCategory} />
        <TextField
          label="Message"
          value={message}
          onChangeText={setMessage}
          placeholder="Describe the issue"
          multiline
          numberOfLines={4}
          textAlignVertical="top"
        />
        <PrimaryButton
          label="Submit support request"
          onPress={() => void submitSupport()}
          isLoading={createSupportMutation.isPending}
          disabled={!message.trim()}
        />
      </GlassCard>

      <GlassCard style={styles.block}>
        <Text style={styles.itemTitle}>Data rights</Text>
        <Text style={styles.itemBody}>Create placeholder records for deletion and export workflows.</Text>
        <SecondaryButton
          label="Request account deletion"
          onPress={() => {
            void (async () => {
              const response = await createDeletionMutation.mutateAsync({
                notes: "User requested deletion from mobile settings."
              });
              setStatusMessage(`Deletion request ${response.id} created.`);
            })();
          }}
          disabled={createDeletionMutation.isPending}
        />
        <SecondaryButton
          label="Request data export"
          onPress={() => {
            void (async () => {
              const response = await createExportMutation.mutateAsync({
                notes: "User requested data export from mobile settings."
              });
              setStatusMessage(`Export request ${response.id} created.`);
            })();
          }}
          disabled={createExportMutation.isPending}
        />
      </GlassCard>

      {statusMessage ? <Text style={styles.status}>{statusMessage}</Text> : null}
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: layout.screenTopPadding
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  block: {
    gap: spacing[8]
  },
  itemTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  itemBody: {
    color: palette.textSecondary,
    ...typography.body2
  },
  status: {
    color: palette.textSecondary,
    ...typography.caption
  }
});
