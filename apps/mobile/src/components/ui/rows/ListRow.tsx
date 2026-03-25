import type { ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { Pressable, View } from "react-native";
import { AppText } from "../text/AppText";
import { useRowPresets } from "./row.presets";

type ListRowProps = {
  title: string;
  subtitle?: string;
  leading?: ReactNode;
  trailing?: ReactNode;
  onPress?: () => void;
  dense?: boolean;
  style?: StyleProp<ViewStyle>;
};

export function ListRow({
  title,
  subtitle,
  leading,
  trailing,
  onPress,
  dense = false,
  style
}: ListRowProps) {
  const rowPresets = useRowPresets();

  return (
    <Pressable
      disabled={!onPress}
      onPress={onPress}
      style={({ pressed }) => [
        rowPresets.container,
        dense ? rowPresets.dense : null,
        style,
        pressed ? rowPresets.selectable : null
      ]}
    >
      {leading}
      <View style={{ flex: 1 }}>
        <AppText preset="body" style={rowPresets.title}>
          {title}
        </AppText>
        {subtitle ? (
          <AppText preset="secondary" style={rowPresets.subtitle}>
            {subtitle}
          </AppText>
        ) : null}
      </View>
      {trailing}
    </Pressable>
  );
}
