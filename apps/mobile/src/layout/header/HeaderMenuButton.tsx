import { Ionicons } from "@expo/vector-icons";
import { requestOpenGlobalAppMenu } from "../../components/layout/GlobalAppMenu";
import { palette } from "../../theme/tokens";
import { HEADER_CONSTANTS } from "./header.constants";
import { HeaderActionButton } from "./HeaderActionButton";

export function HeaderMenuButton() {
  return (
    <HeaderActionButton
      icon={
        <Ionicons
          name="menu-outline"
          size={HEADER_CONSTANTS.iconSize}
          color={palette.accent}
        />
      }
      accessibilityLabel="Open settings menu"
      onPress={requestOpenGlobalAppMenu}
    />
  );
}
