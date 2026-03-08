import { ReactNode } from "react";
import { ScreenContainer } from "./ui/ScreenContainer";

type ScreenProps = {
  children: ReactNode;
};

export function Screen({ children }: ScreenProps) {
  return <ScreenContainer>{children}</ScreenContainer>;
}
