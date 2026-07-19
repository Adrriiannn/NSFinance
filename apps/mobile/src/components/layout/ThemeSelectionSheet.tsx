import { Ionicons } from "@expo/vector-icons";
import { Pressable, View } from "react-native";
import { buildThemeSelectionOptions } from "../../features/theme/themeSelectionOptions";
import { useThemeRuntime } from "../../theme/runtime/ThemeRuntimeProvider";
import { palette, spacing, createRuntimeStyleSheet } from "../../theme/tokens";
import { ModalSheet } from "../ui/surfaces/ModalSheet";
import { AppText } from "../ui/text/AppText";

type ThemeSelectionSheetProps = {
  visible: boolean;
  onClose: () => void;
};

export function ThemeSelectionSheet({ visible, onClose }: ThemeSelectionSheetProps) {
  const { preference, setThemePreference, isTransitioning } = useThemeRuntime();
  const options = buildThemeSelectionOptions(preference);

  return (
    <ModalSheet
      visible={visible}
      onClose={onClose}
      title="Theme"
      subtitle="Choose how NSFinance dresses for you"
    >
      <View style={styles.list}>
        {options.map((option) => (
          <Pressable
            key={option.id}
            accessibilityRole="radio"
            accessibilityLabel={option.label}
            accessibilityHint={option.description}
            accessibilityState={{ selected: option.selected, disabled: isTransitioning }}
            disabled={isTransitioning}
            onPress={() => {
              if (!option.selected) {
                setThemePreference(option.preference);
              }

              onClose();
            }}
            style={({ pressed }) => [
              styles.row,
              option.selected ? styles.rowSelected : null,
              pressed && !isTransitioning ? styles.rowPressed : null,
              isTransitioning ? styles.rowDisabled : null
            ]}
          >
            <View style={styles.rowText}>
              <AppText preset="body" style={styles.rowLabel}>
                {option.label}
              </AppText>
              <AppText preset="secondary">{option.description}</AppText>
            </View>
            <View style={[styles.radio, option.selected ? styles.radioSelected : null]}>
              {option.selected ? <Ionicons name="checkmark" size={12} color="#FFFFFF" /> : null}
            </View>
          </Pressable>
        ))}
      </View>
    </ModalSheet>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  list: {
    gap: spacing[8],
    paddingVertical: spacing[8]
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    paddingHorizontal: spacing[16],
    paddingVertical: spacing[12]
  },
  rowSelected: {
    borderColor: palette.accent
  },
  rowPressed: {
    opacity: 0.85
  },
  rowDisabled: {
    opacity: 0.6
  },
  rowText: {
    flex: 1,
    minWidth: 0,
    gap: spacing[2]
  },
  rowLabel: {
    fontWeight: "600"
  },
  radio: {
    width: 22,
    height: 22,
    borderRadius: 11,
    borderWidth: 1.5,
    borderColor: palette.borderStrong,
    alignItems: "center",
    justifyContent: "center"
  },
  radioSelected: {
    backgroundColor: palette.accent,
    borderColor: palette.accent
  }
}));
