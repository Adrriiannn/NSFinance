import type { ReactNode } from "react";
import { View } from "react-native";
import { spacing } from "../../theme/tokens";
import { TertiaryButton } from "./TertiaryButton";
import { AppText } from "./text/AppText";

type SectionHeaderProps = {
  title: string;
  subtitle?: string;
  actionLabel?: string;
  onActionPress?: () => void;
  trailing?: ReactNode;
};

export function SectionHeader({
  title,
  subtitle,
  actionLabel,
  onActionPress,
  trailing
}: SectionHeaderProps) {
  return (
    <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center", gap: spacing[12] }}>
      <View>
        <AppText preset="sectionTitle">{title}</AppText>
        {subtitle ? <AppText preset="secondary">{subtitle}</AppText> : null}
      </View>

      {trailing}

      {!trailing && actionLabel ? (
        <TertiaryButton label={actionLabel} onPress={onActionPress ?? (() => undefined)} />
      ) : null}
    </View>
  );
}
