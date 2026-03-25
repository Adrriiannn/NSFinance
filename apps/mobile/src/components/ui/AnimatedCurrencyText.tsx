import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, Easing, StyleProp, Text, TextStyle } from "react-native";
import { formatCurrency } from "../../lib/format";
import { useThemeTokens } from "../../theme/tokens";

type AnimatedCurrencyTextProps = {
  value: number;
  currency?: string;
  style?: StyleProp<TextStyle>;
  baseColor?: string;
  increaseColor?: string;
  decreaseColor?: string;
  duration?: number;
};

export function AnimatedCurrencyText({
  value,
  currency = "EUR",
  style,
  baseColor,
  increaseColor,
  decreaseColor,
  duration = 260
}: AnimatedCurrencyTextProps) {
  const { palette } = useThemeTokens();
  const resolvedBaseColor = baseColor ?? palette.textPrimary;
  const resolvedIncreaseColor = increaseColor ?? palette.success;
  const resolvedDecreaseColor = decreaseColor ?? palette.negative;
  const animatedValue = useRef(new Animated.Value(value)).current;
  const previousValueRef = useRef(value);
  const toneResetTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [displayValue, setDisplayValue] = useState(value);
  const [tone, setTone] = useState<"base" | "up" | "down">("base");

  useEffect(() => {
    const listenerId = animatedValue.addListener(({ value: nextValue }) => {
      setDisplayValue(nextValue);
    });

    return () => {
      animatedValue.removeListener(listenerId);
    };
  }, [animatedValue]);

  useEffect(() => {
    const previousValue = previousValueRef.current;
    if (value > previousValue) {
      setTone("up");
    } else if (value < previousValue) {
      setTone("down");
    }

    if (toneResetTimer.current) {
      clearTimeout(toneResetTimer.current);
      toneResetTimer.current = null;
    }

    if (value !== previousValue) {
      toneResetTimer.current = setTimeout(() => {
        setTone("base");
      }, 540);
    }

    Animated.timing(animatedValue, {
      toValue: value,
      duration,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: false
    }).start();

    previousValueRef.current = value;

    return () => {
      if (toneResetTimer.current) {
        clearTimeout(toneResetTimer.current);
      }
    };
  }, [animatedValue, duration, value]);

  const color = useMemo(() => {
    if (tone === "up") {
      return resolvedIncreaseColor;
    }

    if (tone === "down") {
      return resolvedDecreaseColor;
    }

    return resolvedBaseColor;
  }, [resolvedBaseColor, resolvedDecreaseColor, resolvedIncreaseColor, tone]);

  return <Text style={[{ color }, style]}>{formatCurrency(displayValue, currency)}</Text>;
}
