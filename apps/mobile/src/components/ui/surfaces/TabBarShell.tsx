import type { ReactNode } from "react";
import { View } from "react-native";
import type { StyleProp, ViewProps, ViewStyle } from "react-native";
import { useSurfacePresets } from "./surface.presets";

type TabBarShellProps = ViewProps & {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
};

export function TabBarShell({ children, style, ...rest }: TabBarShellProps) {
  const surfacePresets = useSurfacePresets();

  return (
    <View {...rest} style={[surfacePresets.tabBarShell, style]}>
      {children}
    </View>
  );
}
