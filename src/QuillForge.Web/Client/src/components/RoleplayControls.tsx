interface RoleplayControlsProps {
  hasMessages: boolean;
  onRegenerate: () => void;
  onDeleteLast: () => void;
  disabled: boolean;
}

export default function RoleplayControls({ hasMessages, onRegenerate, onDeleteLast, disabled }: RoleplayControlsProps) {
  if (!hasMessages) return null;

  return (
    <div className="flex flex-wrap gap-2">
      <button
        onClick={onRegenerate}
        disabled={disabled}
        className="rounded-lg bg-surface-alt px-4 py-2 text-sm font-medium text-text transition-colors hover:bg-border disabled:opacity-50"
      >
        Regenerate
      </button>
      <button
        onClick={onDeleteLast}
        disabled={disabled}
        className="rounded-lg bg-surface-alt px-4 py-2 text-sm font-medium text-text-muted transition-colors hover:bg-border disabled:opacity-50"
      >
        Delete Last
      </button>
    </div>
  );
}
