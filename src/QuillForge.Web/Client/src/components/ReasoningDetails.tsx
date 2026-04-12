import { useEffect, useState } from "react";
import type { ReasoningArtifact } from "../types";

interface ReasoningDetailsProps {
  reasoning?: string | null;
  artifacts?: ReasoningArtifact[] | null;
  className?: string;
  summaryClassName?: string;
  panelClassName?: string;
  preClassName?: string;
  selectClassName?: string;
}

function artifactKey(artifact: ReasoningArtifact): string {
  return `${artifact.agentId}:${artifact.sequence}`;
}

function selectDefaultArtifact(artifacts?: ReasoningArtifact[] | null): ReasoningArtifact | null {
  if (!artifacts || artifacts.length === 0) {
    return null;
  }

  for (let index = artifacts.length - 1; index >= 0; index -= 1) {
    if (artifacts[index].agentId === "prose-writer") {
      return artifacts[index];
    }
  }

  for (let index = artifacts.length - 1; index >= 0; index -= 1) {
    if (artifacts[index].agentId === "assistant") {
      return artifacts[index];
    }
  }

  let selected = artifacts[0];
  for (const artifact of artifacts) {
    if (artifact.sequence >= selected.sequence) {
      selected = artifact;
    }
  }

  return selected;
}

function getArtifactLabel(artifact: ReasoningArtifact, artifacts: ReasoningArtifact[]): string {
  let duplicateCount = 0;
  let duplicateIndex = 0;

  for (const current of artifacts) {
    if (current.agentId !== artifact.agentId) {
      continue;
    }

    duplicateCount += 1;
    if (current.sequence <= artifact.sequence) {
      duplicateIndex += 1;
    }
  }

  return duplicateCount > 1
    ? `${artifact.agentLabel} ${duplicateIndex}`
    : artifact.agentLabel;
}

export default function ReasoningDetails({
  reasoning,
  artifacts,
  className,
  summaryClassName,
  panelClassName,
  preClassName,
  selectClassName,
}: ReasoningDetailsProps) {
  const [selectedArtifactKey, setSelectedArtifactKey] = useState<string | null>(null);

  useEffect(() => {
    const defaultArtifact = selectDefaultArtifact(artifacts);
    setSelectedArtifactKey(defaultArtifact ? artifactKey(defaultArtifact) : null);
  }, [artifacts]);

  if (!reasoning && (!artifacts || artifacts.length === 0)) {
    return null;
  }

  const defaultArtifact = selectDefaultArtifact(artifacts);
  const selectedArtifact = artifacts?.find((artifact) => artifactKey(artifact) === selectedArtifactKey) ?? defaultArtifact ?? null;
  const displayedReasoning = selectedArtifact?.content ?? reasoning ?? "";
  const hasMultipleArtifacts = (artifacts?.length ?? 0) > 1;
  const summarySuffix = hasMultipleArtifacts ? ` · ${artifacts?.length} agents` : "";

  return (
    <details className={className}>
      <summary className={summaryClassName}>
        reasoning{summarySuffix}
      </summary>
      <div className={panelClassName}>
        {artifacts && artifacts.length > 0 && (
          <div className="flex items-center gap-2 mb-2">
            <span className="text-[10px] uppercase tracking-wider text-text-muted/60">
              agent
            </span>
            {hasMultipleArtifacts ? (
              <select
                value={selectedArtifact ? artifactKey(selectedArtifact) : ""}
                onChange={(event) => setSelectedArtifactKey(event.target.value)}
                className={selectClassName}
              >
                {artifacts.map((artifact) => (
                  <option key={artifactKey(artifact)} value={artifactKey(artifact)}>
                    {getArtifactLabel(artifact, artifacts)}
                  </option>
                ))}
              </select>
            ) : (
              <span className="text-[10px] px-2 py-1 rounded bg-surface/70 border border-border/40 text-text-muted/80">
                {artifacts[0].agentLabel}
              </span>
            )}
          </div>
        )}
        <pre className={preClassName}>{displayedReasoning}</pre>
      </div>
    </details>
  );
}
