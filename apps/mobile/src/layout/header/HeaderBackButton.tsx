import { Ionicons } from "@expo/vector-icons";
import { useNavigation } from "@react-navigation/native";
import { useRouter } from "expo-router";
import { palette } from "../../theme/tokens";
import { HEADER_CONSTANTS } from "./header.constants";
import { HeaderActionButton } from "./HeaderActionButton";

export function HeaderBackButton({ fallbackHref }: { fallbackHref?: string }) {
  const navigation = useNavigation();
  const router = useRouter();

  return (
    <HeaderActionButton
      icon={
        <Ionicons
          name="arrow-back"
          size={HEADER_CONSTANTS.iconSize}
          color={palette.textPrimary}
        />
      }
      accessibilityLabel="Go back"
      onPress={() => {
        if (navigation.canGoBack()) {
          navigation.goBack();
          return;
        }

        if (fallbackHref) {
          router.replace(fallbackHref as never);
          return;
        }

        router.back();
      }}
    />
  );
}

