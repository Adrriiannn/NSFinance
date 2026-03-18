import { useMemo, useState } from "react";
import { Pressable } from "react-native";
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
};

export function ModalSelectField({
  label,
  value,
  options,
  placeholder = "Select",
  onChange,
  disabled = false
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
        onPress={() => setIsOpen(true)}
      />

      <ModalSheet visible={isOpen} onClose={() => setIsOpen(false)} title={label}>
        {options.map((option) => (
          <Pressable
            key={option.value}
            onPress={() => {
              onChange(option.value);
              setIsOpen(false);
            }}
          >
            <ListRow
              title={option.label}
              onPress={() => {
                onChange(option.value);
                setIsOpen(false);
              }}
              trailing={
                option.value === value ? <AppText preset="caption" tone="accent">Selected</AppText> : undefined
              }
            />
          </Pressable>
        ))}
      </ModalSheet>
    </>
  );
}
