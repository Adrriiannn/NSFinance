import { useThemeTokens } from "../../../theme/tokens";
import { View } from "react-native";
import { AppText } from "../text/AppText";
import { useFeedbackPresets, type FeedbackTone } from "./feedback.presets";

type SnackbarProps = {
  message: string;
  tone?: FeedbackTone;
};

export function Snackbar({ message, tone = "info" }: SnackbarProps) {
  const { palette } = useThemeTokens();
  const { feedbackPresets, snackbarTonePresets } = useFeedbackPresets();

  return (
    <View style={[feedbackPresets.snackbar, snackbarTonePresets[tone]]}>
      <AppText
        preset="caption"
        style={{ color: palette.textPrimary, fontWeight: "500", textAlign: "center" }}
      >
        {message}
      </AppText>
    </View>
  );
}
