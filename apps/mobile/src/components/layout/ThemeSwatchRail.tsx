import { Pressable, ScrollView, StyleSheet, View } from "react-native";
import { buildThemeSelectionOptions } from "../../features/theme/themeSelectionOptions";
import { useThemeRuntime } from "../../theme/runtime/ThemeRuntimeProvider";
import { themePacks } from "../../theme/runtime/themePacks";
import type { ThemePreference } from "../../theme/runtime/themePreference";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../theme/tokens";
import { AppText } from "../ui/text/AppText";

// One-tap theme rail (THEME-002 picker overhaul): every theme is a visual
// swatch on a single horizontal rail - no toggle/dropdown split, no second
// navigation level. Selection applies instantly behind the reveal transition.

const SWATCH_SIZE = 44;

function SystemSwatchFace() {
  return (
    <View style={faceStyles.face}>
      <View style={[faceStyles.half, { backgroundColor: themePacks.light.theme.colors.canvas }]} />
      <View style={[faceStyles.half, { backgroundColor: themePacks.dark.theme.colors.canvas }]} />
    </View>
  );
}

function AutomaticSwatchFace() {
  return (
    <View style={faceStyles.face}>
      <View style={faceStyles.quadrantRow}>
        <View
          style={[faceStyles.quadrant, { backgroundColor: themePacks.spring.theme.colors.accent.primary }]}
        />
        <View
          style={[faceStyles.quadrant, { backgroundColor: themePacks.summer.theme.colors.accent.primary }]}
        />
      </View>
      <View style={faceStyles.quadrantRow}>
        <View
          style={[faceStyles.quadrant, { backgroundColor: themePacks.winter.theme.colors.accent.primary }]}
        />
        <View
          style={[faceStyles.quadrant, { backgroundColor: themePacks.autumn.theme.colors.accent.primary }]}
        />
      </View>
    </View>
  );
}

function PackSwatchFace({ preference }: { preference: ThemePreference }) {
  if (preference.kind !== "fixed") {
    return null;
  }

  const pack = themePacks[preference.themeId];

  return (
    <View style={[faceStyles.face, { backgroundColor: pack.theme.colors.canvas }]}>
      <View
        style={[faceStyles.accentCore, { backgroundColor: pack.theme.colors.accent.primary }]}
      />
    </View>
  );
}

export function ThemeSwatchRail() {
  const { preference, setThemePreference, isTransitioning } = useThemeRuntime();
  const options = buildThemeSelectionOptions(preference);

  return (
    <View style={styles.wrap}>
      <AppText preset="fieldLabel" style={styles.title}>
        Theme
      </AppText>
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.rail}
      >
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
            }}
            style={({ pressed }) => [
              styles.swatchItem,
              pressed && !isTransitioning ? styles.swatchPressed : null,
              isTransitioning ? styles.swatchDisabled : null
            ]}
          >
            <View style={[styles.swatchRing, option.selected ? styles.swatchRingSelected : null]}>
              {option.id === "system" ? (
                <SystemSwatchFace />
              ) : option.id === "automatic" ? (
                <AutomaticSwatchFace />
              ) : (
                <PackSwatchFace preference={option.preference} />
              )}
            </View>
            <AppText
              preset="caption"
              numberOfLines={1}
              style={[styles.swatchLabel, option.selected ? styles.swatchLabelSelected : null]}
            >
              {option.id === "automatic" ? "Auto" : option.label}
            </AppText>
          </Pressable>
        ))}
      </ScrollView>
    </View>
  );
}

const faceStyles = StyleSheet.create({
  face: {
    width: SWATCH_SIZE,
    height: SWATCH_SIZE,
    borderRadius: SWATCH_SIZE / 2,
    overflow: "hidden",
    flexDirection: "row",
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: "rgba(127, 127, 127, 0.4)"
  },
  half: {
    flex: 1,
    height: "100%"
  },
  quadrantRow: {
    flex: 1,
    height: "100%"
  },
  quadrant: {
    flex: 1,
    width: "100%"
  },
  accentCore: {
    position: "absolute",
    left: "50%",
    top: "50%",
    width: 16,
    height: 16,
    marginLeft: -8,
    marginTop: -8,
    borderRadius: 8
  }
});

const styles = createRuntimeStyleSheet(() => ({
  wrap: {
    gap: spacing[8]
  },
  title: {
    color: palette.textSecondary
  },
  rail: {
    gap: spacing[12],
    paddingVertical: spacing[4],
    paddingRight: spacing[8]
  },
  swatchItem: {
    alignItems: "center",
    gap: spacing[4],
    width: SWATCH_SIZE + 12
  },
  swatchPressed: {
    opacity: 0.85
  },
  swatchDisabled: {
    opacity: 0.6
  },
  swatchRing: {
    width: SWATCH_SIZE + 8,
    height: SWATCH_SIZE + 8,
    borderRadius: (SWATCH_SIZE + 8) / 2,
    borderWidth: 2,
    borderColor: "transparent",
    alignItems: "center",
    justifyContent: "center"
  },
  swatchRingSelected: {
    borderColor: palette.accent
  },
  swatchLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  swatchLabelSelected: {
    color: palette.accent
  }
}));
