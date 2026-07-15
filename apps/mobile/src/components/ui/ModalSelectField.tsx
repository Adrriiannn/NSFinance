import { useMemo, useState } from "react";
import { AppText } from "./text/AppText";
import { SelectField as BaseSelectField } from "./fields/SelectField";
import { ModalSheet } from "./surfaces/ModalSheet";
import { ListRow } from "./rows/ListRow";

export type ModalSelectOption = {
  label: string;
  value: string;
};

type ModalSelectFieldProps = {
  label: string;
  value: string | null | undefined;
  options: ModalSelectOption[];
  placeholder?: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  helper?: string;
  error?: string;
  sheetMaxHeightRatio?: number;
};

const DEFAULT_MODAL_SELECT_SHEET_MAX_HEIGHT_RATIO = 0.4;

export function ModalSelectField({
  label,
  value,
  options,
  placeholder = "Select",
  onChange,
  disabled = false,
  helper,
  error,
  sheetMaxHeightRatio = DEFAULT_MODAL_SELECT_SHEET_MAX_HEIGHT_RATIO
}: ModalSelectFieldProps) {
  const [isOpen, setIsOpen] = useState(false);

  const selected = useMemo(
    () => options.find((item) => item.value === value),
    [options, value]
  );

  return (
    <>
      <BaseSelectField
        label={label}
        value={selected?.label ?? null}
        placeholder={placeholder}
        disabled={disabled}
        helper={helper}
        error={error}
        onPress={() => setIsOpen(true)}
      />

      <ModalSheet
        visible={isOpen}
        onClose={() => setIsOpen(false)}
        title={label}
        maxHeightRatio={sheetMaxHeightRatio}
      >
        {options.map((option) => (
          <ListRow
            key={option.value}
            title={option.label}
            onPress={() => {
              onChange(option.value);
              setIsOpen(false);
            }}
            trailing={
              option.value === value ? <AppText preset="caption" tone="accent">Selected</AppText> : undefined
            }
          />
        ))}
      </ModalSheet>
    </>
  );
}
