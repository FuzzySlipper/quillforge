import { useEffect, useState } from "react";
import Overlay from "./Overlay";
import {
  listProviders,
  createProvider,
  updateProvider,
  deleteProvider,
  testProvider,
  fetchProviderModels,
  fetchModelsForNew,
  getAgentModels,
  updateAgentModels,
  type ProviderInfo,
  type AgentAssignments,
} from "../api";

interface ProviderManagerProps {
  open: boolean;
  onClose: () => void;
  onChanged: () => void;
}

interface FormState {
  alias: string;
  name: string;
  type: string;
  baseUrl: string;
  modelsUrl: string;
  apiKey: string;
  defaultModel: string;
  contextLimit: number;
}

const EMPTY_FORM: FormState = {
  alias: "",
  name: "",
  type: "anthropic",
  baseUrl: "",
  modelsUrl: "",
  apiKey: "",
  defaultModel: "",
  contextLimit: 128000,
};

const OPTIONS_REFERENCE = `Available options (all optional):

Sampling:
  temperature      — Randomness (0.0–2.0)
  top_p            — Nucleus sampling
  top_k            — Top-k sampling (not all providers)
  frequency_penalty — Penalize repeated tokens
  presence_penalty  — Penalize tokens already present
  repetition_penalty — Combined repetition penalty
  min_p            — Minimum probability cutoff
  seed             — Deterministic sampling seed

Provider quirks:
  reasoning_content   — "auto", true, false
    Add reasoning_content field (DeepSeek reasoner)
  strip_empty_required — "auto", true, false
    Remove empty required arrays from tool schemas
  extra_body          — {...} arbitrary extra request fields

Example:
{
  "temperature": 0.8,
  "reasoning_content": true,
  "extra_body": {"reasoning": {"effort": "high"}}
}`;

/** Suggest initial options based on model name patterns. */
function suggestOptions(model: string, providerType: string): Record<string, unknown> | null {
  if (!model) return null;
  const m = model.toLowerCase();
  const opts: Record<string, unknown> = {};

  // DeepSeek reasoner models need reasoning_content and strip_empty_required
  if (m.includes("deepseek")) {
    opts.strip_empty_required = true;
    if (m.includes("reasoner") || m.includes("r1")) {
      opts.reasoning_content = true;
    }
  }

  // Models with extended thinking / reasoning support
  if (m.includes("reasoner") || m.includes("-r1") || m.includes("thinking")) {
    opts.reasoning_content = true;
  }

  // For OpenAI-compatible providers using reasoning models, add the reasoning effort hint
  if (providerType === "openai" && (m.includes("o1") || m.includes("o3") || m.includes("o4"))) {
    opts.extra_body = { reasoning: { effort: "high" } };
  }

  // Suggest a sensible temperature for creative writing
  if (Object.keys(opts).length === 0) {
    opts.temperature = 0.7;
  }

  return Object.keys(opts).length > 0 ? opts : null;
}

const TEMPLATES = [
  { label: "Anthropic", type: "anthropic", baseUrl: "", modelsUrl: "https://api.anthropic.com/v1/models" },
  { label: "OpenAI", type: "openai", baseUrl: "https://api.openai.com/v1", modelsUrl: "https://api.openai.com/v1/models" },
  { label: "Custom (OpenAI-compatible)", type: "openai", baseUrl: "", modelsUrl: "" },
];

export default function ProviderManager({ open, onClose, onChanged }: ProviderManagerProps) {
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [assignments, setAssignments] = useState<AgentAssignments | null>(null);
  const [editing, setEditing] = useState<string | null>(null); // alias being edited, or "__new__"
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [models, setModels] = useState<string[]>([]);
  const [fetchingModels, setFetchingModels] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [optionsOpen, setOptionsOpen] = useState(false);
  const [optionsText, setOptionsText] = useState("{}");
  const [testing, setTesting] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<{ alias: string; success: boolean; error?: string } | null>(null);

  useEffect(() => {
    if (!open) return;
    refresh();
  }, [open]);

  async function refresh() {
    const [provData, assignData] = await Promise.all([
      listProviders(),
      getAgentModels(),
    ]);
    setProviders(provData.providers);
    setAssignments(assignData.assignments);
  }

  async function handleAssignmentChange(agent: keyof AgentAssignments, alias: string) {
    if (!assignments) return;
    const updates = { [agent]: alias };
    try {
      const result = await updateAgentModels(updates);
      setAssignments(result.assignments);
      await refresh();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update assignment");
    }
  }

  function handleNew(template: typeof TEMPLATES[number]) {
    setForm({
      ...EMPTY_FORM,
      name: template.label,
      type: template.type,
      baseUrl: template.baseUrl,
      modelsUrl: template.modelsUrl,
    });
    setModels([]);
    setError(null);
    setOptionsOpen(false);
    setOptionsText("{}");
    setEditing("__new__");
  }

  function handleEdit(p: ProviderInfo) {
    setForm({
      alias: p.alias,
      name: p.name,
      type: p.type,
      baseUrl: p.baseUrl || "",
      modelsUrl: p.modelsUrl || "",
      apiKey: "",
      defaultModel: p.defaultModel,
      contextLimit: p.contextLimit ?? 128000,
    });
    setModels([]);
    setError(null);
    setOptionsOpen(false);
    setOptionsText(p.options ? JSON.stringify(p.options, null, 2) : "{}");
    setEditing(p.alias);
    // Load cached models
    fetchProviderModels(p.alias)
      .then((data) => setModels(data.models))
      .catch(() => {});
  }

  function handleBack() {
    setEditing(null);
    setForm(EMPTY_FORM);
    setModels([]);
    setError(null);
    setOptionsOpen(false);
    setOptionsText("{}");
  }

  async function handleFetchModels() {
    setFetchingModels(true);
    setError(null);
    try {
      if (editing === "__new__") {
        const data = await fetchModelsForNew({
          type: form.type,
          baseUrl: form.baseUrl || null,
          modelsUrl: form.modelsUrl || null,
          apiKey: form.apiKey || undefined,
        });
        setModels(data.models);
      } else if (editing) {
        const data = await fetchProviderModels(editing);
        setModels(data.models);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch models");
    } finally {
      setFetchingModels(false);
    }
  }

  function parseOptions(): Record<string, unknown> | undefined {
    const trimmed = optionsText.trim();
    if (!trimmed || trimmed === "{}") return undefined;
    try {
      return JSON.parse(trimmed);
    } catch {
      return undefined;
    }
  }

  async function handleSave() {
    // Validate options JSON before saving
    const trimmed = optionsText.trim();
    if (trimmed && trimmed !== "{}") {
      try {
        JSON.parse(trimmed);
      } catch {
        setError("Invalid JSON in options");
        return;
      }
    }

    setSaving(true);
    setError(null);
    try {
      const opts = parseOptions();
      if (editing === "__new__") {
        await createProvider({
          alias: form.alias,
          name: form.name,
          type: form.type,
          baseUrl: form.baseUrl || null,
          modelsUrl: form.modelsUrl || null,
          apiKey: form.apiKey || undefined,
          defaultModel: form.defaultModel,
          contextLimit: form.contextLimit,
          options: opts,
        });
      } else if (editing) {
        const updates: Record<string, unknown> = {};
        if (form.name) updates.name = form.name;
        if (form.defaultModel) updates.defaultModel = form.defaultModel;
        if (form.apiKey) updates.apiKey = form.apiKey;
        if (form.baseUrl !== undefined) updates.baseUrl = form.baseUrl || null;
        if (form.modelsUrl !== undefined) updates.modelsUrl = form.modelsUrl || null;
        updates.contextLimit = form.contextLimit;
        if (opts) updates.options = opts;
        await updateProvider(editing, updates);
      }
      await refresh();
      onChanged();
      handleBack();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    } finally {
      setSaving(false);
    }
  }

  async function handleTest(alias: string) {
    setTesting(alias);
    setTestResult(null);
    try {
      const result = await testProvider(alias);
      setTestResult(result);
    } catch (err) {
      setTestResult({ alias, success: false, error: err instanceof Error ? err.message : "Test failed" });
    } finally {
      setTesting(null);
    }
  }

  async function handleDelete(alias: string) {
    try {
      await deleteProvider(alias);
      await refresh();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  }

  const inputClass =
    "w-full bg-input-bg text-text border border-border rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-accent";

  // ── Edit / New form ───────────────────────────────────────────────
  if (editing) {
    const isNew = editing === "__new__";
    return (
      <Overlay open={open} onClose={onClose} title={isNew ? "Add Provider" : `Edit: ${editing}`}>
        <div className="flex flex-col gap-3">
          <button onClick={handleBack} className="text-sm text-text-muted hover:text-text self-start">
            &larr; Back to list
          </button>

          {isNew && (
            <div>
              <label className="text-xs text-text-muted uppercase tracking-wider">Alias</label>
              <input
                type="text"
                value={form.alias}
                onChange={(e) => setForm({ ...form, alias: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "") })}
                placeholder="e.g. claude, gpt, local-llm"
                className={inputClass}
              />
              <p className="text-[11px] text-text-muted mt-1">
                Used in config to reference this provider. Must be unique.
              </p>
            </div>
          )}

          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Display Name</label>
            <input
              type="text"
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              className={inputClass}
            />
          </div>

          {form.type !== "anthropic" && (
            <div>
              <label className="text-xs text-text-muted uppercase tracking-wider">Base URL</label>
              <input
                type="text"
                value={form.baseUrl}
                onChange={(e) => setForm({ ...form, baseUrl: e.target.value })}
                placeholder="https://api.openai.com/v1"
                className={inputClass}
              />
            </div>
          )}

          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Models URL</label>
            <input
              type="text"
              value={form.modelsUrl}
              onChange={(e) => setForm({ ...form, modelsUrl: e.target.value })}
              placeholder="Auto-detected from provider type"
              className={inputClass}
            />
            <p className="text-[11px] text-text-muted mt-1">
              Endpoint for fetching available models. Leave blank for default.
            </p>
          </div>

          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">
              API Key {!isNew && "(leave blank to keep current)"}
            </label>
            <input
              type="password"
              value={form.apiKey}
              onChange={(e) => setForm({ ...form, apiKey: e.target.value })}
              placeholder={isNew ? "sk-..." : "••••••••"}
              className={inputClass}
            />
          </div>

          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="text-xs text-text-muted uppercase tracking-wider">Model</label>
              <button
                onClick={handleFetchModels}
                disabled={fetchingModels}
                className="text-xs text-accent hover:text-accent-hover disabled:opacity-50"
              >
                {fetchingModels ? "Fetching..." : "Fetch Models"}
              </button>
            </div>
            {models.length > 0 ? (
              <div className="max-h-48 overflow-y-auto border border-border rounded-lg">
                {models.map((m) => (
                  <button
                    key={m}
                    onClick={() => setForm({ ...form, defaultModel: m })}
                    className={`w-full text-left px-3 py-1.5 text-sm transition-colors ${
                      m === form.defaultModel
                        ? "bg-accent/20 text-accent"
                        : "hover:bg-input-bg text-text"
                    }`}
                  >
                    {m}
                  </button>
                ))}
              </div>
            ) : (
              <input
                type="text"
                value={form.defaultModel}
                onChange={(e) => setForm({ ...form, defaultModel: e.target.value })}
                placeholder="model-id (or click Fetch Models)"
                className={inputClass}
              />
            )}
          </div>

          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Context Limit (tokens)</label>
            <input
              type="number"
              value={form.contextLimit}
              onChange={(e) => setForm({ ...form, contextLimit: parseInt(e.target.value) || 128000 })}
              className={inputClass}
              min={1000}
              step={1000}
            />
            <div className="text-[10px] text-text-muted mt-0.5">
              {form.contextLimit >= 1000000 ? `${(form.contextLimit / 1000000).toFixed(1)}M` : `${Math.round(form.contextLimit / 1000)}k`} tokens
            </div>
          </div>

          <div>
            <button
              onClick={() => {
                const opening = !optionsOpen;
                setOptionsOpen(opening);
                // Auto-suggest options when opening with blank/empty options
                if (opening && optionsText.trim() === "{}") {
                  const suggested = suggestOptions(form.defaultModel, form.type);
                  if (suggested) {
                    setOptionsText(JSON.stringify(suggested, null, 2));
                  }
                }
              }}
              className="text-xs text-accent hover:text-accent-hover"
            >
              {optionsOpen ? "Hide Options" : "Options"}
              {optionsText.trim() !== "{}" && " *"}
            </button>
            {optionsOpen && (
              <div className="mt-2 flex flex-col gap-2">
                <pre className="text-[10px] text-text-muted bg-surface-alt/50 rounded-lg px-3 py-2 max-h-40 overflow-y-auto whitespace-pre-wrap">
                  {OPTIONS_REFERENCE}
                </pre>
                <textarea
                  value={optionsText}
                  onChange={(e) => setOptionsText(e.target.value)}
                  placeholder="{}"
                  rows={6}
                  className={`${inputClass} font-mono text-xs resize-y`}
                />
              </div>
            )}
          </div>

          {error && (
            <p className="text-sm text-red-400">{error}</p>
          )}

          <div className="flex gap-2 justify-end pt-2">
            <button onClick={handleBack} className="text-sm text-text-muted hover:text-text px-3 py-1.5">
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={saving || (isNew && (!form.alias || !form.defaultModel))}
              className="text-sm bg-accent text-white rounded-lg px-4 py-1.5 disabled:opacity-50"
            >
              {saving ? "Saving..." : "Save"}
            </button>
          </div>
        </div>
      </Overlay>
    );
  }

  // ── Provider list ─────────────────────────────────────────────────
  return (
    <Overlay open={open} onClose={onClose} title="AI Providers">
      <div className="flex flex-col gap-4">
        {providers.length === 0 ? (
          <p className="text-sm text-text-muted">
            No providers configured. Add one to get started.
          </p>
        ) : (
          <div className="flex flex-col gap-2">
            {providers.map((p) => (
              <div
                key={p.alias}
                className={`flex items-center justify-between px-3 py-2.5 rounded-lg bg-input-bg/50 border ${
                  (p.usedBy ?? []).length > 0 ? "border-accent/40" : "border-border/50"
                }`}
              >
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium text-text">{p.alias}</span>
                    <span className="text-[10px] uppercase tracking-wider text-accent/70 bg-accent/10 px-1.5 py-0.5 rounded">
                      {p.type}
                    </span>
                    {p.apiKeySet === false && (
                      <span className="text-[10px] uppercase tracking-wider text-red-400/80 bg-red-400/10 px-1.5 py-0.5 rounded">
                        No Key
                      </span>
                    )}
                  </div>
                  <div className="text-xs text-text-muted mt-0.5">
                    {p.defaultModel || "no model selected"}
                  </div>
                  {(p.usedBy ?? []).length > 0 && (
                    <div className="text-[10px] text-accent/70 mt-0.5">
                      used by: {(p.usedBy ?? []).join(", ")}
                    </div>
                  )}
                </div>
                <div className="flex gap-1.5 shrink-0">
                  <button
                    onClick={() => handleTest(p.alias)}
                    disabled={testing === p.alias}
                    className={`text-xs px-2 py-1 rounded bg-surface-alt disabled:opacity-50 ${
                      testResult?.alias === p.alias
                        ? testResult.success
                          ? "text-green-400"
                          : "text-red-400"
                        : "text-text-muted hover:text-accent"
                    }`}
                    title={testResult?.alias === p.alias && !testResult.success ? testResult.error : undefined}
                  >
                    {testing === p.alias
                      ? "Testing..."
                      : testResult?.alias === p.alias
                        ? testResult.success ? "OK" : "Fail"
                        : "Test"}
                  </button>
                  <button
                    onClick={() => handleEdit(p)}
                    className="text-xs text-text-muted hover:text-text px-2 py-1 rounded bg-surface-alt"
                  >
                    Edit
                  </button>
                  <button
                    onClick={() => handleDelete(p.alias)}
                    className="text-xs text-text-muted hover:text-red-400 px-2 py-1 rounded bg-surface-alt"
                  >
                    Del
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}

        {testResult && !testResult.success && (
          <p className="text-xs text-red-400 px-1">
            {testResult.alias}: {testResult.error || "Connection failed"}
          </p>
        )}

        {assignments && providers.length > 0 && (
          <div>
            <div className="text-xs text-text-muted uppercase tracking-wider mb-2">Agent Assignments</div>
            <div className="flex flex-col gap-2">
              {(["orchestrator", "proseWriter", "librarian", "research"] as const).map((agent) => (
                <div key={agent} className="flex items-center justify-between gap-3">
                  <span className="text-sm text-text min-w-[100px]">
                    {agent.replace(/([A-Z])/g, " $1").toLowerCase()}
                  </span>
                  <select
                    value={assignments[agent]}
                    onChange={(e) => handleAssignmentChange(agent, e.target.value)}
                    className="flex-1 bg-input-bg text-text border border-border rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:border-accent"
                  >
                    {providers.map((p) => (
                      <option key={p.alias} value={p.alias}>
                        {p.alias} ({p.defaultModel || "no model"})
                      </option>
                    ))}
                    {/* Show current value if it doesn't match any provider (raw model ID) */}
                    {!providers.some((p) => p.alias === assignments[agent]) && (
                      <option value={assignments[agent]}>
                        {assignments[agent]} (not configured)
                      </option>
                    )}
                  </select>
                </div>
              ))}
            </div>
          </div>
        )}

        <div>
          <div className="text-xs text-text-muted uppercase tracking-wider mb-2">Add Provider</div>
          <div className="flex flex-col gap-1">
            {TEMPLATES.map((t) => (
              <button
                key={t.label}
                onClick={() => handleNew(t)}
                className="flex items-center px-3 py-2 rounded-lg hover:bg-input-bg text-left transition-colors text-sm text-text"
              >
                {t.label}
              </button>
            ))}
          </div>
        </div>

        {error && <p className="text-sm text-red-400">{error}</p>}
      </div>
    </Overlay>
  );
}
