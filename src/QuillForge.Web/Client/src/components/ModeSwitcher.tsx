import { useEffect, useState } from "react";
import Overlay from "./Overlay";
import { getMode, getProjects, setMode, listCharacterCards, listResearchProjects, type CharacterCardSummary } from "../api";
import type { Mode, ModeInfo } from "../types";
import { MODE_DESCRIPTIONS, MODE_ICON_PATHS, MODE_LABELS } from "../modePresentation";

interface ModeSwitcherProps {
  open: boolean;
  onClose: () => void;
  onSwitched: (sessionId?: string | null, notice?: string | null) => void;
  sessionId?: string | null;
}

export default function ModeSwitcher({ open, onClose, onSwitched, sessionId }: ModeSwitcherProps) {
  const [current, setCurrent] = useState<ModeInfo | null>(null);
  const [selectedMode, setSelectedMode] = useState<Mode>("guide");
  const [projects, setProjects] = useState<string[]>([]);
  const [project, setProject] = useState("");
  const [saving, setSaving] = useState(false);
  const [validationMessage, setValidationMessage] = useState<string | null>(null);

  // Roleplay character selection
  const [characters, setCharacters] = useState<CharacterCardSummary[]>([]);
  const [selectedCharacter, setSelectedCharacter] = useState("");

  useEffect(() => {
    if (!open) return;
    getMode(sessionId).then((m) => {
      setCurrent(m);
      setSelectedMode(m.mode);
      setProject(m.project || "");
      setValidationMessage(null);
    });
  }, [open, sessionId]);

  useEffect(() => {
    if (!open) return;
    if (selectedMode === "roleplay") {
      getProjects(selectedMode).then((p) => setProjects(p.projects ?? []));
      listCharacterCards(sessionId).then((data) => {
        setCharacters(data.cards);
        setSelectedCharacter(data.activeAi || "");
      });
    } else if (selectedMode === "research") {
      listResearchProjects().then((p) => setProjects(p.projects ?? []));
    } else if (selectedMode !== "guide" && selectedMode !== "council" && selectedMode !== "games") {
      // Fetch projects for writer/forge
      getProjects(selectedMode).then((p) => setProjects(p.projects ?? []));
    }
  }, [open, selectedMode, sessionId]);

  async function handleApply() {
    const validation = getValidationMessage();
    if (validation) {
      setValidationMessage(validation);
      return;
    }

    setSaving(true);
    try {
      let result;
      if (selectedMode === "roleplay") {
        result = await setMode(
          selectedMode,
          project || undefined,
          buildRoleplayFileName(selectedCharacter, characters),
          selectedCharacter || undefined,
          sessionId,
        );
      } else {
        result = await setMode(selectedMode, project || undefined, undefined, undefined, sessionId);
      }
      onSwitched(result?.sessionId ?? sessionId, result?.notice ?? null);
      onClose();
    } catch (err) {
      setValidationMessage(err instanceof Error ? err.message : "Mode switch failed.");
    } finally {
      setSaving(false);
    }
  }

  function getValidationMessage(): string | null {
    if (selectedMode !== "roleplay") {
      return null;
    }

    if (!project) {
      return "Choose or type a project before starting roleplay.";
    }

    if (!selectedCharacter) {
      return characters.length === 0
        ? "Create or import a character card before starting roleplay."
        : "Choose the AI character before starting roleplay.";
    }

    return null;
  }

  const needsProject = selectedMode === "writer" || selectedMode === "forge" || selectedMode === "research" || selectedMode === "roleplay";
  const needsCharacter = selectedMode === "roleplay";
  const canApply =
    selectedMode === "guide" ||
    selectedMode === "council" ||
    ((needsProject ? !!project : true) &&
      (needsCharacter ? !!selectedCharacter : true));

  return (
    <Overlay open={open} onClose={onClose} title="Mode">
      {current ? (
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap gap-2">
            {(Object.keys(MODE_LABELS) as Mode[]).map((m) => (
              <button
                key={m}
                onClick={() => {
                  setSelectedMode(m);
                  setValidationMessage(null);
                }}
                className={`flex-1 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                  selectedMode === m
                    ? "bg-accent text-accent-contrast"
                    : "bg-input-bg text-text-muted hover:text-text border border-border"
                }`}
              >
                <span className="flex items-center justify-center gap-2">
                  <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-surface-alt/60 ring-1 ring-border/40">
                    <img src={MODE_ICON_PATHS[m]} alt="" aria-hidden="true" className="h-5 w-5" />
                  </span>
                  <span>{MODE_LABELS[m]}</span>
                </span>
              </button>
            ))}
          </div>

          <p className="text-sm text-text-muted">{MODE_DESCRIPTIONS[selectedMode]}</p>

          {/* Project-based modes: project selector */}
          {needsProject && (
            <label className="flex flex-col gap-1">
              <span className="text-sm text-text-muted">Project</span>
              <div className="flex gap-2">
                <select
                  value={project}
                  onChange={(e) => {
                    setProject(e.target.value);
                    setValidationMessage(null);
                  }}
                  className="flex-1 bg-input-bg text-text border border-border rounded-lg px-3 py-2"
                >
                  <option value="">Select or type new...</option>
                  {projects.map((p) => (
                    <option key={p} value={p}>{p}</option>
                  ))}
                </select>
                <input
                  type="text"
                  value={project}
                  onChange={(e) => {
                    setProject(e.target.value);
                    setValidationMessage(null);
                  }}
                  placeholder="New project"
                  className="flex-1 bg-input-bg text-text border border-border rounded-lg px-3 py-2"
                />
              </div>
            </label>
          )}

          {/* Roleplay: character selector */}
          {needsCharacter && (
            <label className="flex flex-col gap-1">
              <span className="text-sm text-text-muted">Character (AI plays)</span>
              {characters.length === 0 ? (
                <p className="text-sm text-text-muted/70">
                  No character cards yet. Create one in the Characters menu first.
                </p>
              ) : (
                <div className="flex flex-col gap-1.5 mt-1">
                  {characters.map((c) => (
                    <button
                      key={c.fileName}
                      onClick={() => {
                        setSelectedCharacter(c.fileName);
                        setValidationMessage(null);
                      }}
                      className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-left transition-colors ${
                        selectedCharacter === c.fileName
                          ? "bg-accent/20 ring-1 ring-accent/50"
                          : "hover:bg-input-bg bg-input-bg/30"
                      }`}
                    >
                      {c.portrait ? (
                        <img
                          src={`/content/character-cards/${c.portrait}`}
                          alt=""
                          className="w-9 h-9 rounded-full object-cover ring-1 ring-border shrink-0"
                        />
                      ) : (
                        <div className="w-9 h-9 rounded-full bg-surface-alt ring-1 ring-border shrink-0 flex items-center justify-center text-text-muted text-xs">
                          ?
                        </div>
                      )}
                      <span className={`text-sm ${selectedCharacter === c.fileName ? "text-accent" : "text-text"}`}>
                        {c.name}
                      </span>
                    </button>
                  ))}
                </div>
              )}
            </label>
          )}

          {validationMessage && (
            <div role="alert" className="rounded-lg border border-warning/40 bg-warning-soft px-3 py-2 text-sm text-warning-text">
              {validationMessage}
            </div>
          )}

          <button
            onClick={handleApply}
            disabled={saving || (selectedMode !== "roleplay" && !canApply)}
            className="mt-2 bg-accent hover:bg-accent-hover text-accent-contrast font-semibold rounded-lg px-4 py-2.5 disabled:opacity-50 transition-colors"
          >
            {saving ? "Switching..." : "Apply"}
          </button>
        </div>
      ) : (
        <p className="text-text-muted">Loading...</p>
      )}
    </Overlay>
  );
}

function buildRoleplayFileName(characterFileName: string, characters: CharacterCardSummary[]): string {
  const card = characters.find((character) => character.fileName === characterFileName);
  const basis = characterFileName.replace(/\.[^/.]+$/, "") || card?.name || "roleplay";
  const slug = slugifyFilePart(basis);
  return `${slug}-roleplay.md`;
}

function slugifyFilePart(value: string): string {
  const slug = value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return slug || "roleplay";
}
