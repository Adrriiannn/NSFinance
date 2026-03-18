import { Chip as BaseChip } from "./chips/Chip";

type ChipProps = {
  label: string;
  selected?: boolean;
  onPress?: () => void;
  compact?: boolean;
};

export function Chip({ label, selected = false, onPress, compact = false }: ChipProps) {
  return (
    <BaseChip
      label={label}
      selected={selected}
      onPress={onPress}
      variant={compact ? "compact" : "filter"}
      tone={selected ? "info" : "default"}
    />
  );
}
