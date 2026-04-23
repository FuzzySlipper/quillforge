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
  canRegenerate?: boolean;
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
  canRegenerate = true,
}: WriterControlsProps) {
  const capturedTarget = pendingProject && pendingFile ? `story/${pendingProject}/${pendingFile}` : null;
  const currentTarget = currentProject && currentFile ? `story/${currentProject}/${currentFile}` : null;
  const targetChanged = capturedTarget !== null && currentTarget !== null && capturedTarget !== currentTarget;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="qf-shell-folio">{hasPending ? "Pending Review" : "Draft State"}</div>
          <p className="mt-2 text-sm leading-6 text-text-muted">
            {hasPending
              ? "Quill has produced draft content that is waiting for an explicit accept or reject decision."
              : "No pending draft is waiting for review. You can keep working with Quill or ask for a fresh pass."}
          </p>
        </div>
        <span className={`qf-shell-card px-3 py-1.5 text-[12px] ${hasPending ? "text-accent" : "text-text-muted"}`}>
          {hasPending ? "awaiting review" : "clear"}
        </span>
      </div>

      <div className="space-y-2 text-[13px] leading-6 text-text-muted">
        {currentTarget ? (
          <div>
            Current target: <code className="font-mono text-[11px] text-text">{currentTarget}</code>
          </div>
        ) : (
          <div>No writer target is active yet. Pick a project and file from the mode menu.</div>
        )}
        {capturedTarget && (
          <div>
            Pending save target: <code className="font-mono text-[11px] text-text">{capturedTarget}</code>
          </div>
        )}
        {targetChanged && currentTarget && (
          <div className="text-amber-200">
            Pending content was captured for a different target than the one currently selected.
          </div>
        )}
      </div>

      <div className="flex flex-wrap gap-2">
        {hasPending && (
          <>
            <button
              onClick={onAccept}
              disabled={disabled}
              className="rounded-lg bg-green-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-green-600 disabled:opacity-50"
            >
              Accept
            </button>
            <button
              onClick={onReject}
              disabled={disabled}
              className="rounded-lg bg-red-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-600 disabled:opacity-50"
            >
              Reject
            </button>
          </>
        )}
        <button
          onClick={onRegenerate}
          disabled={disabled || !canRegenerate}
          className="rounded-lg bg-surface-alt px-4 py-2 text-sm font-medium text-text transition-colors hover:bg-border disabled:opacity-50"
        >
          Regenerate
        </button>
      </div>
    </div>
  );
}
