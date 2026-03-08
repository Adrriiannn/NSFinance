import { ReactNode } from "react";
import { GlassCard } from "./ui/GlassCard";

type CardProps = {
  children: ReactNode;
};

export function Card({ children }: CardProps) {
  return <GlassCard>{children}</GlassCard>;
}
