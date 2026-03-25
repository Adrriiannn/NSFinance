import { Ionicons } from "@expo/vector-icons";
import { Pressable, Text, TextInput, View } from "react-native";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../../theme/tokens";
import type {
  ActivityCategoryTokenValue,
  ActivitySearchToken as ActivitySearchTokenModel
} from "../search/activitySearch.types";

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
  const categoryValue =
    token.type === "category" ? (token.value as ActivityCategoryTokenValue) : null;
  const isCategoryPlaceholder =
    token.type === "category" && !categoryValue?.domainName?.trim();
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
          selectionColor={palette.accent}
          cursorColor={palette.accent}
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
          <View
            style={[
              isCategoryPlaceholder ? styles.valueHintWrap : null,
              isCategoryPlaceholder ? styles.valueHintUnderline : null
            ]}
          >
            <Text
              numberOfLines={1}
              style={[styles.value, isCategoryPlaceholder ? styles.valueHint : null]}
            >
              {token.displayValue || "Set value"}
            </Text>
          </View>
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

const styles = createRuntimeStyleSheet(() => ({
  token: {
    minHeight: 28,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[6],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[4],
    maxWidth: 228
  },
  tokenActive: {
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.fieldStrong
  },
  label: {
    color: palette.accent,
    ...typography.caption,
    fontWeight: "500"
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
  valueHint: {
    color: palette.textSecondary
  },
  valueHintWrap: {
    alignSelf: "flex-start"
  },
  valueHintUnderline: {
    borderBottomWidth: 1,
    borderBottomColor: palette.accent,
    paddingBottom: 1
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
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center"
  },
  removeButtonPressed: {
    opacity: 0.8
  }
}));

