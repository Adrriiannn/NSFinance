import { Ionicons } from "@expo/vector-icons";
import { useThemeTokens } from "../../../theme/tokens";
import { ListRow } from "./ListRow";

type SettingsRowProps = {
  title: string;
  subtitle?: string;
  icon?: React.ReactNode;
  onPress?: () => void;
};

export function SettingsRow({ title, subtitle, icon, onPress }: SettingsRowProps) {
  const { palette } = useThemeTokens();

  return (
    <ListRow
      title={title}
      subtitle={subtitle}
      leading={icon}
      trailing={<Ionicons name="chevron-forward" size={18} color={palette.textSecondary} />}
      onPress={onPress}
    />
  );
}
