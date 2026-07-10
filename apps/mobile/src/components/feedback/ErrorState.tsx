import { PrimaryButton } from "../ui/PrimaryButton";
import { GlassCard } from "../ui/GlassCard";
import { Banner } from "../ui/feedback/Banner";

type ErrorStateProps = {
  title?: string;
  message?: string;
  onRetry?: () => void;
  retryLabel?: string;
};

export function ErrorState({
  title = "Something went wrong",
  message = "We couldn't load this section.",
  onRetry,
  retryLabel = "Retry"
}: ErrorStateProps) {
  return (
    <GlassCard style={{ gap: 12 }}>
      <Banner title={title} message={message} tone="error" />
      {onRetry ? <PrimaryButton label={retryLabel} onPress={onRetry} /> : null}
    </GlassCard>
  );
}
