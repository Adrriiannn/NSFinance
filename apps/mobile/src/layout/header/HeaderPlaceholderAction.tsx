import { View } from "react-native";
import { HEADER_CONSTANTS } from "./header.constants";

export function HeaderPlaceholderAction() {
  return <View style={{ width: HEADER_CONSTANTS.trailingSlotWidth, height: HEADER_CONSTANTS.touchTarget }} />;
}

