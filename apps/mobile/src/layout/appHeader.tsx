import { Ionicons } from "@expo/vector-icons";
import { useNavigation } from "@react-navigation/native";
import { useRouter } from "expo-router";
import { useMemo, useState, type ReactNode } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import type { StyleProp, TextInputProps, ViewStyle } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { requestOpenGlobalAppMenu } from "../components/layout/GlobalAppMenu";
import { TextField } from "../components/ui/fields/TextField";
import { ListRow } from "../components/ui/rows/ListRow";
import { ModalSheet } from "../components/ui/surfaces/ModalSheet";
import { layout, palette, spacing, surfaces, zIndex } from "../theme/tokens";
import { useOptionalAdaptiveShell } from "./adaptive/adaptive.hooks";

type HeaderPresetName =
  | "primaryDefault"
  | "primaryGreeting"
  | "primaryTwoRowSelector"
  | "primaryTwoRowSearch"
  | "secondaryDetail";

type HeaderActionButtonProps = {
  icon?: ReactNode;
  label?: string;
  onPress?: () => void;
  accessibilityLabel?: string;
  variant?: "icon" | "compact";
  style?: StyleProp<ViewStyle>;
};

type HeaderDropdownOption = {
  label: string;
  value: string;
};

type HeaderDropdownSlotProps = {
  title: string;
  value: string | null | undefined;
  placeholder?: string;
  options?: HeaderDropdownOption[];
  onChange?: (value: string) => void;
  onPress?: () => void;
  containerStyle?: StyleProp<ViewStyle>;
  disabled?: boolean;
};

type HeaderSearchSlotProps = TextInputProps & {
  containerStyle?: StyleProp<ViewStyle>;
  onClear?: () => void;
};

type HeaderShellProps = {
  preset: HeaderPresetName;
  title: string;
  subtitle?: string;
  includeTopInset?: boolean;
  bleedHorizontal?: number;
  elevated?: boolean;
  leadingAction?: ReactNode;
  trailingAction?: ReactNode;
  secondRow?: ReactNode;
  style?: StyleProp<ViewStyle>;
  contentStyle?: StyleProp<ViewStyle>;
  hideDivider?: boolean;
};

const HEADER_CONTROL_HEIGHT = 36;

const HEADER = {
  rowHeight: 56,
  secondRowHeight: 44,
  touchTarget: 44,
  paddingX: 12,
  contentGap: 12,
  secondRowGap: 8,
  rowGap: -10,
  titleSubtitleGap: 4,
  leadingSlotWidth: 44,
  trailingSlotWidth: 44,
  iconSize: 20,
  iconButtonRadius: 14,
  titleMaxWidthDefault: "56%",
  titleMaxWidthCentered: "62%",
  subtitleMaxWidth: "72%",
  greetingTitleMaxWidth: "72%",
  greetingSubtitleMaxWidth: "100%",
  greetingGap: 2,
  inlineButtonHeight: 36,
  inlineButtonRadius: 18,
  iconButtonSize: 36,
  iconButtonVisualRadius: 12,
  dropdownHeight: HEADER_CONTROL_HEIGHT,
  dropdownRadius: 12,
  searchHeight: HEADER_CONTROL_HEIGHT,
  searchRadius: 12,
  stickyDividerHeight: 1,
  stickyElevatedOpacity: 0.94,
  zIndex: zIndex.tabBar + 5
} as const;

function HeaderPlaceholderAction() {
  return <View style={{ width: HEADER.trailingSlotWidth, height: HEADER.touchTarget }} />;
}

function HeaderMenuButton() {
  return (
    <HeaderActionButton
      icon={<Ionicons name="menu-outline" size={HEADER.iconSize} color={palette.textPrimary} />}
      accessibilityLabel="Open settings menu"
      onPress={requestOpenGlobalAppMenu}
    />
  );
}

function HeaderBackButton() {
  const navigation = useNavigation();
  const router = useRouter();

  return (
    <HeaderActionButton
      icon={<Ionicons name="arrow-back" size={HEADER.iconSize} color={palette.textPrimary} />}
      accessibilityLabel="Go back"
      onPress={() => {
        if (navigation.canGoBack()) {
          navigation.goBack();
          return;
        }

        router.back();
      }}
    />
  );
}

export function HeaderActionButton({
  icon,
  label,
  onPress,
  accessibilityLabel,
  variant = "icon",
  style
}: HeaderActionButtonProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      onPress={onPress}
      style={({ pressed }) => [
        variant === "compact" ? styles.compactButton : styles.iconButton,
        pressed ? styles.pressed : null,
        style
      ]}
    >
      {icon}
      {label ? <Text style={styles.compactButtonText}>{label}</Text> : null}
    </Pressable>
  );
}

export function HeaderDropdownSlot({
  title,
  value,
  placeholder = "Select",
  options,
  onChange,
  onPress,
  containerStyle,
  disabled = false
}: HeaderDropdownSlotProps) {
  const [isOpen, setIsOpen] = useState(false);
  const selected = useMemo(
    () => options?.find((option) => option.value === value) ?? null,
    [options, value]
  );

  return (
    <>
      <Pressable
        disabled={disabled}
        onPress={() => {
          if (disabled) {
            return;
          }

          if (onPress) {
            onPress();
            return;
          }

          if (options?.length) {
            setIsOpen(true);
          }
        }}
        style={({ pressed }) => [
          styles.fieldSlot,
          disabled ? styles.disabled : null,
          pressed ? styles.pressed : null,
          containerStyle
        ]}
      >
        <Text
          numberOfLines={1}
          style={[styles.dropdownText, !selected?.label && !value ? styles.placeholderText : null]}
        >
          {selected?.label ?? value ?? placeholder}
        </Text>
        <Ionicons name="chevron-down" size={16} color={palette.textSecondary} />
      </Pressable>

      {options?.length ? (
        <ModalSheet visible={isOpen} onClose={() => setIsOpen(false)} title={title}>
          <View style={{ gap: spacing[8] }}>
            {options.map((option) => (
              <ListRow
                key={option.value}
                title={option.label}
                onPress={() => {
                  onChange?.(option.value);
                  setIsOpen(false);
                }}
                trailing={
                  option.value === value ? (
                    <Text style={styles.selectedText}>Selected</Text>
                  ) : undefined
                }
              />
            ))}
          </View>
        </ModalSheet>
      ) : null}
    </>
  );
}

export function HeaderSearchSlot({
  containerStyle,
  onClear,
  value,
  ...props
}: HeaderSearchSlotProps) {
  return (
    <View style={[styles.searchSlotWrap, containerStyle]}>
      <TextField
        {...props}
        value={value}
        showLabel={false}
        dense
        placeholderTextColor={palette.textSecondary}
        containerStyle={styles.searchSlot}
        inputStyle={styles.searchInput}
        leading={<Ionicons name="search-outline" size={18} color={palette.textSecondary} />}
        trailing={
          value && onClear ? (
            <Pressable onPress={onClear} style={styles.clearButton}>
              <Ionicons name="close" size={16} color={palette.textSecondary} />
            </Pressable>
          ) : undefined
        }
      />
    </View>
  );
}

export function HeaderShell({
  preset,
  title,
  subtitle,
  includeTopInset = false,
  bleedHorizontal,
  elevated = false,
  leadingAction,
  trailingAction,
  secondRow,
  style,
  contentStyle,
  hideDivider = false
}: HeaderShellProps) {
  const insets = useSafeAreaInsets();
  const adaptiveShell = useOptionalAdaptiveShell();
  const isGreeting = preset === "primaryGreeting";
  const isSecondary = preset === "secondaryDetail";
  const hasSecondRow = preset === "primaryTwoRowSelector" || preset === "primaryTwoRowSearch";
  const centeredTitle = preset !== "primaryGreeting";
  const resolvedLeading = leadingAction ?? (isSecondary ? <HeaderBackButton /> : <HeaderMenuButton />);
  const resolvedTrailing = trailingAction ?? <HeaderPlaceholderAction />;
  const resolvedBleedHorizontal =
    bleedHorizontal ?? adaptiveShell?.metrics.contentHorizontalPadding ?? layout.screenHorizontalPadding;
  const shouldAutoDockTop = isSecondary && !includeTopInset && !adaptiveShell;

  return (
    <View
      style={[
        styles.shell,
        {
          marginHorizontal: -resolvedBleedHorizontal,
          marginTop: shouldAutoDockTop ? -insets.top : 0,
          paddingTop: includeTopInset || shouldAutoDockTop ? insets.top : 0,
          opacity: elevated ? HEADER.stickyElevatedOpacity : 1
        },
        style
      ]}
    >
      <View style={contentStyle}>
        <View style={styles.row}>
          <View style={styles.leadingSlot}>{resolvedLeading}</View>
          <View style={styles.titleSlot}>
            <View
              style={[
                styles.titleWrap,
                centeredTitle ? styles.titleCentered : styles.titleLeading,
                {
                  maxWidth: centeredTitle
                    ? HEADER.titleMaxWidthCentered
                    : isGreeting
                      ? HEADER.greetingTitleMaxWidth
                      : HEADER.titleMaxWidthDefault
                }
              ]}
            >
              <Text
                numberOfLines={1}
                adjustsFontSizeToFit
                minimumFontScale={0.84}
                style={[
                  isGreeting ? styles.greetingTitle : styles.title,
                  centeredTitle ? styles.titleCenteredText : null
                ]}
              >
                {title}
              </Text>
              {subtitle ? (
                <Text
                  numberOfLines={1}
                  adjustsFontSizeToFit
                  minimumFontScale={0.92}
                  style={[
                    styles.subtitle,
                    {
                      marginTop: isGreeting ? HEADER.greetingGap : HEADER.titleSubtitleGap,
                      maxWidth: isGreeting ? HEADER.greetingSubtitleMaxWidth : HEADER.subtitleMaxWidth,
                      textAlign: centeredTitle ? "center" : "left"
                    }
                  ]}
                >
                  {subtitle}
                </Text>
              ) : null}
            </View>
          </View>
          <View style={styles.trailingSlot}>{resolvedTrailing}</View>
        </View>

        {hasSecondRow && secondRow ? (
          <View style={styles.secondRow}>
            <View style={styles.secondRowContent}>{secondRow}</View>
          </View>
        ) : null}
      </View>

      {!hideDivider ? <View style={styles.divider} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  shell: {
    zIndex: HEADER.zIndex,
    backgroundColor: surfaces.app
  },
  row: {
    minHeight: HEADER.rowHeight,
    paddingHorizontal: HEADER.paddingX,
    flexDirection: "row",
    alignItems: "center",
    gap: HEADER.contentGap
  },
  secondRow: {
    marginTop: HEADER.rowGap
  },
  secondRowContent: {
    minHeight: HEADER.secondRowHeight,
    paddingHorizontal: HEADER.paddingX,
    flexDirection: "row",
    alignItems: "center",
    gap: HEADER.secondRowGap
  },
  leadingSlot: {
    width: HEADER.leadingSlotWidth,
    minWidth: HEADER.leadingSlotWidth,
    alignItems: "flex-start",
    justifyContent: "center"
  },
  titleSlot: {
    flex: 1
  },
  trailingSlot: {
    width: HEADER.trailingSlotWidth,
    minWidth: HEADER.trailingSlotWidth,
    alignItems: "flex-end",
    justifyContent: "center"
  },
  titleWrap: {
    flexShrink: 1
  },
  titleCentered: {
    alignItems: "center",
    marginHorizontal: "auto"
  },
  titleLeading: {
    alignItems: "flex-start"
  },
  title: {
    color: palette.textPrimary,
    fontSize: 18,
    lineHeight: 22,
    fontWeight: "700"
  },
  titleCenteredText: {
    textAlign: "center"
  },
  greetingTitle: {
    color: palette.textPrimary,
    fontSize: 22,
    lineHeight: 26,
    fontWeight: "700"
  },
  subtitle: {
    color: palette.textSecondary,
    fontSize: 12,
    lineHeight: 16,
    fontWeight: "500"
  },
  divider: {
    height: HEADER.stickyDividerHeight,
    backgroundColor: "rgba(220,232,255,0.18)"
  },
  iconButton: {
    width: HEADER.iconButtonSize,
    height: HEADER.iconButtonSize,
    borderRadius: HEADER.iconButtonVisualRadius,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.82)",
    alignItems: "center",
    justifyContent: "center"
  },
  compactButton: {
    minHeight: HEADER.inlineButtonHeight,
    borderRadius: HEADER.inlineButtonRadius,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: 14,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8]
  },
  compactButtonText: {
    color: palette.textPrimary,
    fontSize: 13,
    lineHeight: 16,
    fontWeight: "700"
  },
  fieldSlot: {
    flex: 1,
    height: HEADER.dropdownHeight,
    minHeight: HEADER.dropdownHeight,
    borderRadius: HEADER.dropdownRadius,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  dropdownText: {
    flex: 1,
    color: palette.textPrimary,
    fontSize: 14,
    lineHeight: 18,
    fontWeight: "600"
  },
  placeholderText: {
    color: palette.textSecondary
  },
  selectedText: {
    color: palette.primaryGlow,
    fontSize: 13,
    lineHeight: 16,
    fontWeight: "700"
  },
  searchSlot: {
    height: HEADER.searchHeight,
    minHeight: HEADER.searchHeight,
    borderRadius: HEADER.searchRadius,
    paddingHorizontal: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field
  },
  searchSlotWrap: {
    flex: 1,
    minWidth: 0
  },
  searchInput: {
    paddingVertical: 0,
    fontSize: 14,
    lineHeight: 18,
    fontWeight: "600"
  },
  clearButton: {
    width: 24,
    height: 24,
    alignItems: "center",
    justifyContent: "center"
  },
  disabled: {
    opacity: 0.56
  },
  pressed: {
    opacity: 0.88
  }
});
