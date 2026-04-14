interface WriterControlsProps {
  hasPending: boolean;
  currentProject: string | null;
  currentFile: string | null;
  pendingProject: string | null;
  pendingFile: string | null;
  onAccept: () => void;
  onReject: () => void;
  onRegenerate: () => void;
  disabled: boolean;
}

export default function WriterControls({
  hasPending,
  currentProject,
  currentFile,
  pendingProject,
  pendingFile,
  onAccept,
  onReject,
  onRegenerate,
  disabled,
}: WriterControlsProps) {
  if (!hasPending) return null;

  const capturedTarget = pendingProject && pendingFile ? `story/${pendingProject}/${pendingFile}` : null;
  const currentTarget = currentProject && currentFile ? `story/${currentProject}/${currentFile}` : null;
  const targetChanged = capturedTarget !== null && currentTarget !== null && capturedTarget !== currentTarget;

  return (
    <div className="border-t border-border bg-surface">
      {capturedTarget && (
        <div className={`px-3 py-2 text-xs border-b border-border ${targetChanged ? "bg-amber-950/40 text-amber-100" : "bg-surface-alt/70 text-text-muted"}`}>
          <div>
            Pending draft will save to <code className="font-mono text-[11px]">{capturedTarget}</code>.
          </div>
          {targetChanged && currentTarget && (
            <div className="mt-1">
              Current Writer target is <code className="font-mono text-[11px]">{currentTarget}</code>.
            </div>
          )}
        </div>
      )}
      <div className="flex gap-2 px-3 py-2">
        <button
          onClick={onAccept}
          disabled={disabled}
          className="flex-1 bg-green-700 hover:bg-green-600 text-white font-medium rounded-lg px-4 py-2 text-sm disabled:opacity-50 transition-colors"
        >
          Accept
        </button>
        <button
          onClick={onReject}
          disabled={disabled}
          className="flex-1 bg-red-700 hover:bg-red-600 text-white font-medium rounded-lg px-4 py-2 text-sm disabled:opacity-50 transition-colors"
        >
          Reject
        </button>
        <button
          onClick={onRegenerate}
          disabled={disabled}
          className="flex-1 bg-surface-alt hover:bg-border text-text font-medium rounded-lg px-4 py-2 text-sm disabled:opacity-50 transition-colors"
        >
          Regenerate
        </button>
      </div>
    </div>
  );
}
