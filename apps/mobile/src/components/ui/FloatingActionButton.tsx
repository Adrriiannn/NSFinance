import type { ReactNode } from "react";
import { FloatingActionButton as BaseFloatingActionButton } from "./surfaces/FloatingActionButton";

type FloatingActionButtonProps = {
  label: string;
  icon: ReactNode;
  onPress: () => void;
  bottomOffset?: number;
};

export function FloatingActionButton(props: FloatingActionButtonProps) {
  return <BaseFloatingActionButton {...props} />;
}
