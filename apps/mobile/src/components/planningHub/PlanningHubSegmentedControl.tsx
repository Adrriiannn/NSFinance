import { useEffect, useRef, useState } from "react";
import { Animated, Pressable, StyleSheet, Text, View } from "react-native";
import { palette, radius, spacing, typography } from "../../theme/tokens";

type SegmentedOption<T extends string> = {
  label: string;
  value: T;
};

type PlanningHubSegmentedControlProps<T extends string> = {
  label?: string;
  value: T;
  options: SegmentedOption<T>[];
  onChange: (value: T) => void;
};

export function PlanningHubSegmentedControl<T extends string>({
  label,
  value,
  options,
  onChange
}: PlanningHubSegmentedControlProps<T>) {
  const [segmentLayouts, setSegmentLayouts] = useState<Partial<Record<T, { x: number; width: number }>>>({});
  const highlightLeft = useRef(new Animated.Value(0)).current;
  const highlightWidth = useRef(new Animated.Value(0)).current;
  const hasAnimatedRef = useRef(false);

  useEffect(() => {
    const layout = segmentLayouts[value];
    if (!layout) {
      return;
    }

    if (!hasAnimatedRef.current) {
      highlightLeft.setValue(layout.x);
      highlightWidth.setValue(layout.width);
      hasAnimatedRef.current = true;
      return;
    }

    Animated.parallel([
      Animated.timing(highlightLeft, {
        toValue: layout.x,
        duration: 210,
        useNativeDriver: false
      }),
      Animated.timing(highlightWidth, {
        toValue: layout.width,
        duration: 210,
        useNativeDriver: false
      })
    ]).start();
  }, [highlightLeft, highlightWidth, segmentLayouts, value]);

  return (
    <View style={styles.wrapper}>
      {label ? <Text style={styles.label}>{label}</Text> : null}
      <View style={styles.segmentedRow}>
        {segmentLayouts[value] ? (
          <Animated.View
            pointerEvents="none"
            style={[
              styles.segmentHighlight,
              {
                left: highlightLeft,
                width: highlightWidth
              }
            ]}
          />
        ) : null}
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <Pressable
              key={option.value}
              onPress={() => onChange(option.value)}
              onLayout={(event) => {
                const { x, width } = event.nativeEvent.layout;
                setSegmentLayouts((current) => {
                  const previous = current[option.value];
                  if (previous?.x === x && previous?.width === width) {
                    return current;
                  }

                  return {
                    ...current,
                    [option.value]: { x, width }
                  };
                });
              }}
              style={({ pressed }) => [
                styles.segment,
                pressed ? styles.segmentPressed : null
              ]}
            >
              <Text style={[styles.segmentLabel, selected ? styles.segmentLabelSelected : null]}>
                {option.label}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing[8]
  },
  label: {
    color: palette.textPrimary,
    ...typography.caption
  },
  segmentedRow: {
    flexDirection: "row",
    gap: spacing[8],
    padding: 4,
    position: "relative",
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.88)"
  },
  segmentHighlight: {
    position: "absolute",
    top: 4,
    bottom: 4,
    borderRadius: radius.small,
    backgroundColor: "rgba(47,107,255,0.34)",
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.5)"
  },
  segment: {
    flex: 1,
    minHeight: 42,
    borderRadius: radius.small,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12],
    zIndex: 1
  },
  segmentPressed: {
    opacity: 0.92
  },
  segmentLabel: {
    color: palette.textSecondary,
    ...typography.body2,
    fontWeight: "600"
  },
  segmentLabelSelected: {
    color: palette.textPrimary
  }
});
