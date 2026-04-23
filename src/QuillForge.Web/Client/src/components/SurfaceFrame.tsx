import type { ReactNode } from "react";
import Overlay from "./Overlay";

export type SurfaceVariant = "overlay" | "inline";

interface SurfaceFrameProps {
  open: boolean;
  onClose: () => void;
  title: string;
  variant?: SurfaceVariant;
  children: ReactNode;
}

export default function SurfaceFrame({
  open,
  onClose,
  title,
  variant = "overlay",
  children,
}: SurfaceFrameProps) {
  if (!open) {
    return null;
  }

  if (variant === "overlay") {
    return (
      <Overlay open={open} onClose={onClose} title={title}>
        {children}
      </Overlay>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-border/50 px-4 py-3">
        <h2 className="text-base font-semibold text-text">{title}</h2>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">
        {children}
      </div>
    </div>
  );
}
