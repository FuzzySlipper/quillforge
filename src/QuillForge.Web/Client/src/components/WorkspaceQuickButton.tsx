interface WorkspaceQuickButtonProps {
  label: string;
  disabled?: boolean;
  className?: string;
  onClick: () => void;
}

export default function WorkspaceQuickButton({
  label,
  disabled,
  className,
  onClick,
}: WorkspaceQuickButtonProps) {
  const buttonClassName = ["qf-shell-quiet-button", className].filter(Boolean).join(" ");

  return (
    <button type="button" disabled={disabled} onClick={onClick} className={buttonClassName}>
      {label}
    </button>
  );
}
