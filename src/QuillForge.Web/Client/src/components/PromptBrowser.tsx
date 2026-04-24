import { useEffect, useState } from "react";
import MDEditor from "@uiw/react-md-editor";
import { listAssistantPrompts, readAssistantPrompt, writeAssistantPrompt, type AssistantPromptInfo } from "../api";
import { listNarrativeRules, readNarrativeRules, writeNarrativeRules, type NarrativeRulesInfo } from "../api";
import { listWritingStyles, readWritingStyle, writeWritingStyle, type WritingStyleInfo } from "../api";
import SurfaceFrame, { type SurfaceVariant } from "./SurfaceFrame";

interface PromptBrowserProps {
  open: boolean;
  onClose: () => void;
  onChanged: () => void;
  variant?: SurfaceVariant;
}

type Tab = "assistant" | "narrative" | "writing";

export default function PromptBrowser({
  open,
  onClose,
  onChanged,
  variant = "overlay",
}: PromptBrowserProps) {
  const [tab, setTab] = useState<Tab>("assistant");
  const [assistantFiles, setAssistantFiles] = useState<AssistantPromptInfo[]>([]);
  const [narrativeRulesFiles, setNarrativeRulesFiles] = useState<NarrativeRulesInfo[]>([]);
  const [styleFiles, setStyleFiles] = useState<WritingStyleInfo[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [selectedType, setSelectedType] = useState<Tab>("assistant");
  const [content, setContent] = useState("");
  const [originalContent, setOriginalContent] = useState("");
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    listAssistantPrompts().then((data) => setAssistantFiles(data.files));
    listNarrativeRules().then((data) => setNarrativeRulesFiles(data.files));
    listWritingStyles().then((data) => setStyleFiles(data.files));
  }, [open]);

  async function handleSelectStyle(name: string) {
    setLoading(true);
    try {
      const data = await readWritingStyle(name);
      setSelected(name);
      setSelectedType("writing");
      setContent(data.content);
      setOriginalContent(data.content);
    } finally {
      setLoading(false);
    }
  }

  async function handleSelectNarrativeRules(name: string) {
    setLoading(true);
    try {
      const data = await readNarrativeRules(name);
      setSelected(name);
      setSelectedType("narrative");
      setContent(data.content);
      setOriginalContent(data.content);
    } finally {
      setLoading(false);
    }
  }

  async function handleSelectAssistantPrompt(name: string) {
    setLoading(true);
    try {
      const data = await readAssistantPrompt(name);
      setSelected(name);
      setSelectedType("assistant");
      setContent(data.content);
      setOriginalContent(data.content);
    } finally {
      setLoading(false);
    }
  }

  async function handleSave() {
    if (!selected) return;
    setSaving(true);
    try {
      if (selectedType === "assistant") {
        await writeAssistantPrompt(selected, content);
        const data = await listAssistantPrompts();
        setAssistantFiles(data.files);
      } else if (selectedType === "narrative") {
        await writeNarrativeRules(selected, content);
        const data = await listNarrativeRules();
        setNarrativeRulesFiles(data.files);
      } else {
        await writeWritingStyle(selected, content);
        const data = await listWritingStyles();
        setStyleFiles(data.files);
      }
      setOriginalContent(content);
      onChanged();
    } finally {
      setSaving(false);
    }
  }

  function handleBack() {
    setSelected(null);
    setContent("");
    setOriginalContent("");
  }

  const isDirty = content !== originalContent;
  const totalAssistantTokens = assistantFiles.reduce((sum, f) => sum + f.tokens, 0);
  const totalNarrativeRulesTokens = narrativeRulesFiles.reduce((sum, f) => sum + f.tokens, 0);
  const totalStyleTokens = styleFiles.reduce((sum, f) => sum + f.tokens, 0);

  const tabClass = (t: Tab) =>
    `text-sm px-3 py-1.5 rounded-lg transition-colors ${
      tab === t ? "bg-accent text-accent-contrast" : "text-text-muted hover:text-text"
    }`;

  if (selected) {
    return (
      <SurfaceFrame open={open} onClose={onClose} title={selected} variant={variant}>
        <div className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <button
              onClick={handleBack}
              className="text-sm text-text-muted hover:text-text"
            >
              &larr; Back to list
            </button>
            <span className="text-xs text-text-muted">
              ~{Math.round(content.length / 4)} tokens
            </span>
          </div>
          {loading ? (
            <p className="text-text-muted">Loading...</p>
          ) : (
            <>
              <div data-color-mode="dark">
                <MDEditor
                  value={content}
                  onChange={(val) => setContent(val ?? "")}
                  height={400}
                  preview="edit"
                  visibleDragbar
                />
              </div>
              {isDirty && (
                <div className="flex gap-2 justify-end">
                  <button
                    onClick={() => setContent(originalContent)}
                    className="text-sm text-text-muted hover:text-text px-3 py-1.5"
                  >
                    Discard
                  </button>
                  <button
                    onClick={handleSave}
                    disabled={saving}
                    className="text-sm bg-accent text-accent-contrast rounded-lg px-4 py-1.5 disabled:opacity-50"
                  >
                    {saving ? "Saving..." : "Save"}
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      </SurfaceFrame>
    );
  }

  return (
    <SurfaceFrame open={open} onClose={onClose} title="Prompts" variant={variant}>
      <div className="flex flex-col gap-3">
        <div className="flex gap-2">
          <button onClick={() => setTab("assistant")} className={tabClass("assistant")}>
            Assistant
          </button>
          <button onClick={() => setTab("narrative")} className={tabClass("narrative")}>
            Narrative Rules
          </button>
          <button onClick={() => setTab("writing")} className={tabClass("writing")}>
            Writing Styles
          </button>
        </div>

        {tab === "assistant" && (
          <>
            <div className="text-xs text-text-muted">
              {assistantFiles.length} files · ~{Math.round(totalAssistantTokens / 1000)}k tokens total
            </div>
            {assistantFiles.length === 0 ? (
              <p className="text-sm text-text-muted">No assistant prompt files yet.</p>
            ) : (
              <div className="flex flex-col">
                {assistantFiles.map((f) => (
                  <button
                    key={f.name}
                    onClick={() => handleSelectAssistantPrompt(f.name)}
                    className="flex items-center justify-between px-3 py-2 rounded-lg hover:bg-input-bg text-left transition-colors"
                  >
                    <span className="text-sm text-text">{f.name}</span>
                    <span className="text-xs text-text-muted">~{f.tokens} tok</span>
                  </button>
                ))}
              </div>
            )}
          </>
        )}

        {tab === "narrative" && (
          <>
            <div className="text-xs text-text-muted">
              {narrativeRulesFiles.length} files · ~{Math.round(totalNarrativeRulesTokens / 1000)}k tokens total
            </div>
            {narrativeRulesFiles.length === 0 ? (
              <p className="text-sm text-text-muted">No narrative rules files yet.</p>
            ) : (
              <div className="flex flex-col">
                {narrativeRulesFiles.map((f) => (
                  <button
                    key={f.name}
                    onClick={() => handleSelectNarrativeRules(f.name)}
                    className="flex items-center justify-between px-3 py-2 rounded-lg hover:bg-input-bg text-left transition-colors"
                  >
                    <span className="text-sm text-text">{f.name}</span>
                    <span className="text-xs text-text-muted">~{f.tokens} tok</span>
                  </button>
                ))}
              </div>
            )}
          </>
        )}

        {tab === "writing" && (
          <>
            <div className="text-xs text-text-muted">
              {styleFiles.length} files · ~{Math.round(totalStyleTokens / 1000)}k tokens total
            </div>
            {styleFiles.length === 0 ? (
              <p className="text-sm text-text-muted">No writing style files yet.</p>
            ) : (
              <div className="flex flex-col">
                {styleFiles.map((f) => (
                  <button
                    key={f.name}
                    onClick={() => handleSelectStyle(f.name)}
                    className="flex items-center justify-between px-3 py-2 rounded-lg hover:bg-input-bg text-left transition-colors"
                  >
                    <span className="text-sm text-text">{f.name}</span>
                    <span className="text-xs text-text-muted">~{f.tokens} tok</span>
                  </button>
                ))}
              </div>
            )}
          </>
        )}
      </div>
    </SurfaceFrame>
  );
}
