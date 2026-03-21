import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, TextInput, View } from "react-native";
import { palette, spacing, typography } from "../../../theme/tokens";
import type { ActivitySearchToken as ActivitySearchTokenModel } from "../search/activitySearch.types";

type ActivitySearchTokenProps = {
  token: ActivitySearchTokenModel;
  isActive: boolean;
  activeDraft: string;
  onDraftChange: (value: string) => void;
  onSubmitDraft: (options?: { reopenDropdown?: boolean }) => void;
  onRemove: (tokenId: string) => void;
  onPressToken: (tokenId: string) => void;
  onOpenCategoryPicker: () => void;
};

export function ActivitySearchToken({
  token,
  isActive,
  activeDraft,
  onDraftChange,
  onSubmitDraft,
  onRemove,
  onPressToken,
  onOpenCategoryPicker
}: ActivitySearchTokenProps) {
  const showDraftInput =
    isActive &&
    (token.type === "transaction" ||
      token.type === "merchant" ||
      token.type === "date" ||
      token.type === "amount");
  const draftVisualChars = Math.max(activeDraft.length, 0);
  const draftInputWidth = Math.min(156, Math.max(40, Math.round(22 + draftVisualChars * 7)));

  return (
    <View style={[styles.token, isActive ? styles.tokenActive : null]}>
      <Text style={styles.label}>{token.label}:</Text>

      {showDraftInput ? (
        <TextInput
          autoFocus
          value={activeDraft}
          onChangeText={onDraftChange}
          onSubmitEditing={() => onSubmitDraft({ reopenDropdown: false })}
          blurOnSubmit={false}
          returnKeyType="search"
          keyboardType={token.type === "amount" ? "decimal-pad" : "default"}
          placeholder={
            token.type === "amount"
              ? "12.99"
              : token.type === "date"
                ? "value"
                : "value"
          }
          placeholderTextColor={palette.textSecondary}
          style={[styles.tokenInput, { width: draftInputWidth }]}
        />
      ) : (
        <Pressable
          onPress={() => {
            if (token.type === "category") {
              onOpenCategoryPicker();
              return;
            }

            onPressToken(token.id);
          }}
          style={({ pressed }) => [styles.valueWrap, pressed ? styles.valuePressed : null]}
        >
          <Text numberOfLines={1} style={styles.value}>
            {token.displayValue || "Set value"}
          </Text>
        </Pressable>
      )}

      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`Remove ${token.label} filter`}
        onPress={() => onRemove(token.id)}
        style={({ pressed }) => [styles.removeButton, pressed ? styles.removeButtonPressed : null]}
      >
        <Ionicons name="close" size={14} color={palette.textSecondary} />
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  token: {
    minHeight: 28,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(23,44,69,0.92)",
    paddingHorizontal: spacing[6],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[4],
    maxWidth: 228
  },
  tokenActive: {
    borderColor: "rgba(127,174,255,0.7)",
    backgroundColor: "rgba(32,59,94,0.94)"
  },
  label: {
    color: palette.textSecondary,
    ...typography.caption,
    fontWeight: "700"
  },
  valueWrap: {
    flexShrink: 1
  },
  valuePressed: {
    opacity: 0.9
  },
  value: {
    color: palette.textPrimary,
    ...typography.caption
  },
  tokenInput: {
    color: palette.textPrimary,
    ...typography.caption,
    paddingVertical: 0,
    paddingHorizontal: 0,
    maxWidth: 156
  },
  removeButton: {
    width: 18,
    height: 18,
    borderRadius: 9,
    alignItems: "center",
    justifyContent: "center"
  },
  removeButtonPressed: {
    opacity: 0.8
  }
});
