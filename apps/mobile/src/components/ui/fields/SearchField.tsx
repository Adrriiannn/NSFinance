import { Ionicons } from "@expo/vector-icons";
import type { TextInputProps } from "react-native";
import { palette } from "../../../theme/tokens";
import { TextField } from "./TextField";

type SearchFieldProps = TextInputProps & {
  label?: string;
  helper?: string;
  error?: string;
};

export function SearchField(props: SearchFieldProps) {
  return (
    <TextField
      {...props}
      leading={<Ionicons name="search-outline" size={18} color={palette.textSecondary} />}
    />
  );
}
