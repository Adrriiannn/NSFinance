import { Image, StyleProp, StyleSheet, View, ViewStyle } from "react-native";
import { palette, radius } from "../../theme/tokens";

type NsfLogoProps = {
  size?: number;
  style?: StyleProp<ViewStyle>;
};

export function NsfLogo({ size = 56, style }: NsfLogoProps) {
  return (
    <View style={[styles.wrap, style, { width: size, height: size, borderRadius: size * 0.5 }]}>
      <Image
        source={require("../../../assets/nsf-logo.png")}
        style={{ width: size, height: size, borderRadius: size * 0.24 }}
        resizeMode="cover"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    borderWidth: 1,
    borderColor: palette.borderStrong,
    overflow: "hidden",
    borderRadius: radius.medium
  }
});
