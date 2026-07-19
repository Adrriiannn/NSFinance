import { Button } from "../ui/buttons/Button";
import { Card } from "../ui/cards/Card";
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
    <Card style={{ gap: 12 }}>
      <Banner title={title} message={message} tone="error" />
      {onRetry ? <Button label={retryLabel} onPress={onRetry} /> : null}
    </Card>
  );
}
