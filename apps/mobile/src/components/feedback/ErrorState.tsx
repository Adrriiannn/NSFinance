import { View } from "react-native";
import { PrimaryButton } from "../ui/PrimaryButton";
import { GlassCard } from "../ui/GlassCard";
import { Banner } from "../ui/feedback/Banner";
import { AppText } from "../ui/text/AppText";

type ErrorStateProps = {
  title?: string;
  message?: string;
  onRetry?: () => void;
  retryLabel?: string;
  debugDetail?: string;
  showDebugDetail?: boolean;
};

export function ErrorState({
  title = "Something went wrong",
  message = "We couldn't load this section.",
  onRetry,
  retryLabel = "Retry",
  debugDetail,
  showDebugDetail
}: ErrorStateProps) {
  const shouldShowDebug = Boolean(debugDetail) && (showDebugDetail ?? __DEV__);

  return (
    <GlassCard style={{ gap: 12 }}>
      <Banner title={title} message={message} tone="error" />
      {shouldShowDebug ? (
        <View>
          <AppText preset="caption" tone="accent">
            {debugDetail}
          </AppText>
        </View>
      ) : null}
      {onRetry ? <PrimaryButton label={retryLabel} onPress={onRetry} /> : null}
    </GlassCard>
  );
}
