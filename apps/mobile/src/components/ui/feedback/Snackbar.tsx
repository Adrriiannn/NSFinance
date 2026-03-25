import { palette } from "../../../theme/tokens";
import { View } from "react-native";
import { AppText } from "../text/AppText";
import { bannerPresets, feedbackPresets, type FeedbackTone } from "./feedback.presets";

type SnackbarProps = {
  message: string;
  tone?: FeedbackTone;
};

export function Snackbar({ message, tone = "info" }: SnackbarProps) {
  return (
    <View style={[feedbackPresets.snackbar, bannerPresets[tone]]}>
      <AppText preset="caption" style={{ color: palette.textPrimary, fontWeight: "500" }}>
        {message}
      </AppText>
    </View>
  );
}
