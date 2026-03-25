import { Ionicons } from "@expo/vector-icons";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Image, Pressable, ScrollView, Text, View } from "react-native";
import * as ImagePicker from "expo-image-picker";
import { ErrorState } from "../../../src/components/feedback/ErrorState";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { ModalSelectField } from "../../../src/components/ui/ModalSelectField";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import { SecondaryButton } from "../../../src/components/ui/SecondaryButton";
import { TextField } from "../../../src/components/ui/TextField";
import { HeaderShell } from "../../../src/layout/appHeader";
import {
  useBankConnectionsQuery,
  useLinkedBankAccountsQuery
} from "../../../src/features/banking/useBanking";
import {
  useCreateSupportRequestMutation,
  useMySupportRequestsQuery
} from "../../../src/features/support/useSupport";
import { useUserProfileQuery } from "../../../src/features/users/useUserSettings";
import { formatUnknownError } from "../../../src/lib/api/errors";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../../src/theme/tokens";
import type { SupportScreenshotUploadRequest } from "../../../src/types/api";

const supportTaxonomy: Record<string, { label: string; options: { label: string; value: string }[] }> = {
  bank_connection_issue: {
    label: "Bank connection",
    options: [
      { label: "My bank is not listed", value: "my_bank_is_not_listed" },
      { label: "I cannot connect my bank", value: "i_cannot_connect_my_bank" },
      { label: "My connection expired", value: "my_connection_expired" },
      { label: "Reconnection is failing", value: "reconnection_is_failing" },
      { label: "Connected accounts are missing", value: "connected_accounts_are_missing" },
      { label: "Other", value: "other" }
    ]
  },
  missing_transactions: {
    label: "Missing transactions",
    options: [
      { label: "Recent transactions are missing", value: "recent_transactions_are_missing" },
      { label: "Older transactions are missing", value: "older_transactions_are_missing" },
      { label: "A transaction disappeared", value: "a_transaction_disappeared" },
      { label: "Only some accounts are affected", value: "only_some_accounts_are_affected" },
      { label: "Other", value: "other" }
    ]
  },
  incorrect_balances: {
    label: "Incorrect balances",
    options: [
      { label: "Balance looks too high", value: "balance_looks_too_high" },
      { label: "Balance looks too low", value: "balance_looks_too_low" },
      { label: "Currency looks wrong", value: "currency_looks_wrong" },
      { label: "Balance is outdated", value: "balance_is_outdated" },
      { label: "Other", value: "other" }
    ]
  },
  account_security_issue: {
    label: "Account/security",
    options: [
      { label: "I cannot log in", value: "i_cannot_log_in" },
      { label: "I think someone accessed my account", value: "possible_account_access" },
      { label: "I want to change credentials", value: "change_credentials" },
      { label: "I was logged out unexpectedly", value: "logged_out_unexpectedly" },
      { label: "Other", value: "other" }
    ]
  },
  app_bug: {
    label: "App bug",
    options: [
      { label: "Screen layout problem", value: "screen_layout_problem" },
      { label: "Button/action not working", value: "button_action_not_working" },
      { label: "App is slow or freezing", value: "app_slow_or_freezing" },
      { label: "Crash or forced close", value: "crash_or_forced_close" },
      { label: "Other", value: "other" }
    ]
  },
  general_question: {
    label: "General question",
    options: [
      { label: "How bank connections work", value: "how_bank_connections_work" },
      { label: "Data/privacy question", value: "data_privacy_question" },
      { label: "Subscription / billing question", value: "subscription_or_billing_question" },
      { label: "Feature question", value: "feature_question" },
      { label: "Other", value: "other" }
    ]
  }
};

const categoryOptions = [
  { label: "Select issue category", value: "" },
  ...Object.entries(supportTaxonomy).map(([value, entry]) => ({ label: entry.label, value }))
];

type PendingAttachment = SupportScreenshotUploadRequest & {
  previewUri: string;
};

function formatDateTime(value: string) {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return "-";
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false
  }).format(parsed);
}

export default function AccountSupportScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ connectionId?: string; linkedBankAccountId?: string }>();
  const createSupportMutation = useCreateSupportRequestMutation();
  const myRequestsQuery = useMySupportRequestsQuery();
  const profileQuery = useUserProfileQuery();
  const connectionsQuery = useBankConnectionsQuery();
  const linkedAccountsQuery = useLinkedBankAccountsQuery();

  const [category, setCategory] = useState("");
  const [subcategory, setSubcategory] = useState("");
  const [title, setTitle] = useState("");
  const [message, setMessage] = useState("");
  const [contactEmail, setContactEmail] = useState("");
  const [selectedConnectionId, setSelectedConnectionId] = useState<string | null>(null);
  const [selectedLinkedAccountId, setSelectedLinkedAccountId] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [attachments, setAttachments] = useState<PendingAttachment[]>([]);

  useEffect(() => {
    if (profileQuery.data?.primaryEmail && !contactEmail) {
      setContactEmail(profileQuery.data.primaryEmail);
    }
  }, [contactEmail, profileQuery.data?.primaryEmail]);

  useEffect(() => {
    if (params.connectionId) {
      setSelectedConnectionId(params.connectionId);
    }

    if (params.linkedBankAccountId) {
      setSelectedLinkedAccountId(params.linkedBankAccountId);
    }
  }, [params.connectionId, params.linkedBankAccountId]);

  const subcategoryOptions = useMemo(
    () =>
      category
        ? [{ label: "Select issue detail", value: "" }, ...supportTaxonomy[category].options]
        : [{ label: "Select category first", value: "" }],
    [category]
  );

  const connectionOptions = useMemo(
    () => [
      { label: "No specific bank", value: "" },
      ...(connectionsQuery.data ?? []).map((item) => ({
        label: `${item.providerDisplayName || item.provider} (${item.status})`,
        value: item.id
      }))
    ],
    [connectionsQuery.data]
  );

  const linkedAccountOptions = useMemo(() => {
    const linkedAccounts = linkedAccountsQuery.data ?? [];
    const filtered = selectedConnectionId
      ? linkedAccounts.filter((item) => item.connectionId === selectedConnectionId)
      : linkedAccounts;

    return [
      { label: "No specific account", value: "" },
      ...filtered.map((item) => ({
        label: `${item.displayName} (${item.currency})`,
        value: item.id
      }))
    ];
  }, [linkedAccountsQuery.data, selectedConnectionId]);

  const attachScreenshot = async () => {
    if (attachments.length >= 3) {
      return;
    }

    const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      setStatusMessage("Gallery access is required to attach screenshots.");
      return;
    }

    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ["images"],
      quality: 0.75,
      base64: true
    });

    if (result.canceled || !result.assets.length) {
      return;
    }

    const asset = result.assets[0];
    if (!asset.base64) {
      setStatusMessage("Could not read image data. Please try another screenshot.");
      return;
    }

    const extension = asset.uri.toLowerCase().endsWith(".png")
      ? "png"
      : asset.uri.toLowerCase().endsWith(".webp")
        ? "webp"
        : "jpg";
    const contentType =
      extension === "png"
        ? "image/png"
        : extension === "webp"
          ? "image/webp"
          : "image/jpeg";

    const nextAttachment: PendingAttachment = {
      fileName: `support-screenshot-${Date.now()}.${extension}`,
      contentType,
      base64Data: asset.base64,
      previewUri: asset.uri
    };

    setAttachments((current) => [...current, nextAttachment].slice(0, 3));
  };

  const submitSupport = async () => {
    const response = await createSupportMutation.mutateAsync({
      category,
      subcategory,
      title: title.trim(),
      message: message.trim(),
      contactEmail: contactEmail.trim() || null,
      connectionId: selectedConnectionId || null,
      linkedBankAccountId: selectedLinkedAccountId || null,
      screenshots: attachments.map((item) => ({
        fileName: item.fileName,
        contentType: item.contentType,
        base64Data: item.base64Data
      }))
    });

    setStatusMessage(`Support request ${response.id} created with diagnostics attached.`);
    setTitle("");
    setMessage("");
    setAttachments([]);
    setCategory("");
    setSubcategory("");
    await myRequestsQuery.refetch();
  };

  return (
    <ScreenContainer
      contentStyle={styles.content}
      withBottomTabOffset
      bottomInsetOffset={spacing[12]}
      scrollable={false}
    >
      <HeaderShell preset="secondaryDetail" title="Support" />

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
        {createSupportMutation.isError ? (
          <ErrorState
            title="Support request failed"
            message={formatUnknownError(createSupportMutation.error)}
            onRetry={submitSupport}
            retryLabel="Try again"
          />
        ) : null}

        <GlassCard style={styles.block}>
          <Text style={styles.itemTitle}>Report an issue</Text>
          <Text style={styles.itemBody}>Diagnostics will be included to help us investigate.</Text>

          <ModalSelectField
            label="Issue category"
            value={category}
            options={categoryOptions}
            onChange={(value) => {
              setCategory(value);
              setSubcategory("");
            }}
          />
          <ModalSelectField
            label="Issue detail"
            value={subcategory}
            options={subcategoryOptions}
            onChange={setSubcategory}
            disabled={!category}
          />

          <TextField
            label="Short title"
            value={title}
            onChangeText={setTitle}
            placeholder="Brief summary of the issue"
          />
          <TextField
            label="Detailed description"
            value={message}
            onChangeText={setMessage}
            placeholder="Tell us what happened and what you expected"
            multiline
            numberOfLines={5}
            textAlignVertical="top"
          />
          <TextField
            label="Contact email (optional)"
            value={contactEmail}
            onChangeText={setContactEmail}
            keyboardType="email-address"
            autoCapitalize="none"
          />

          <ModalSelectField
            label="Linked bank context"
            value={selectedConnectionId ?? ""}
            options={connectionOptions}
            onChange={(value) => {
              const normalized = value || null;
              setSelectedConnectionId(normalized);
              setSelectedLinkedAccountId(null);
            }}
          />
          <ModalSelectField
            label="Linked account context"
            value={selectedLinkedAccountId ?? ""}
            options={linkedAccountOptions}
            onChange={(value) => setSelectedLinkedAccountId(value || null)}
          />

          <View style={styles.attachmentsHeader}>
            <Text style={styles.itemBody}>Screenshots (up to 3)</Text>
            <SecondaryButton label="Attach screenshot" onPress={() => void attachScreenshot()} />
          </View>
          <View style={styles.attachmentsRow}>
            {attachments.map((item) => (
              <View key={item.fileName} style={styles.attachmentItem}>
                <Image source={{ uri: item.previewUri }} style={styles.attachmentImage} />
                <Pressable
                  style={styles.removeAttachment}
                  onPress={() =>
                    setAttachments((current) =>
                      current.filter((attachment) => attachment.fileName !== item.fileName)
                    )
                  }
                >
                  <Ionicons name="close" size={12} color={palette.textPrimary} />
                </Pressable>
              </View>
            ))}
          </View>

          <PrimaryButton
            label="Submit support request"
            onPress={() => {
              void submitSupport();
            }}
            isLoading={createSupportMutation.isPending}
            disabled={!title.trim() || !message.trim() || !category || !subcategory}
          />
        </GlassCard>

        <GlassCard style={styles.block}>
          <Text style={styles.itemTitle}>Help center</Text>
          <Text style={styles.itemBody}>How bank connections work</Text>
          <Text style={styles.helpText}>
            Connections run through Open Banking providers like TrueLayer. Consent is granted in your bank flow and can be disconnected from Security.
          </Text>
          <Text style={styles.itemBody}>What to do if balances look wrong</Text>
          <Text style={styles.helpText}>
            Trigger a sync, verify the connected bank status, and include the affected account in your report so diagnostics include sync details.
          </Text>
          <SecondaryButton label="Open Security settings" onPress={() => router.push("/(tabs)/accounts/security")} />
        </GlassCard>

        <GlassCard style={styles.block}>
          <Text style={styles.itemTitle}>Recent support requests</Text>
          {(myRequestsQuery.data ?? []).length === 0 ? (
            <Text style={styles.itemBody}>No support requests submitted yet.</Text>
          ) : (
            (myRequestsQuery.data ?? []).slice(0, 5).map((request) => (
              <View key={request.id} style={styles.requestRow}>
                <Text style={styles.requestTitle}>{request.title}</Text>
                <Text style={styles.helpText}>
                  {request.category} / {request.subcategory} | {request.status}
                </Text>
                <Text style={styles.helpText}>{formatDateTime(request.createdUtc)}</Text>
              </View>
            ))
          )}
        </GlassCard>

        {statusMessage ? <Text style={styles.status}>{statusMessage}</Text> : null}
      </ScrollView>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    paddingTop: 0
  },
  scrollContent: {
    gap: spacing[12],
    paddingTop: spacing[10]
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: spacing[16]
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
  helpText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  requestRow: {
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    padding: spacing[12],
    backgroundColor: "rgba(21,21,21,0.66)",
    gap: spacing[4]
  },
  requestTitle: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  status: {
    color: palette.success,
    ...typography.caption
  },
  attachmentsHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[8]
  },
  attachmentsRow: {
    flexDirection: "row",
    gap: spacing[8],
    flexWrap: "wrap"
  },
  attachmentItem: {
    width: 74,
    height: 74,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    overflow: "hidden",
    position: "relative"
  },
  attachmentImage: {
    width: "100%",
    height: "100%"
  },
  removeAttachment: {
    position: "absolute",
    right: 2,
    top: 2,
    width: 18,
    height: 18,
    borderRadius: 6,
    backgroundColor: "rgba(9,20,35,0.9)",
    alignItems: "center",
    justifyContent: "center"
  }
}));



