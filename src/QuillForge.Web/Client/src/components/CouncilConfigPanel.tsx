import { useEffect, useState } from "react";
import Overlay from "./Overlay";
import {
  listCouncilMembers,
  createCouncilMember,
  updateCouncilMember,
  deleteCouncilMember,
  listProviders,
  type CouncilMemberInfo,
  type ProviderInfo,
} from "../api";

interface CouncilConfigPanelProps {
  open: boolean;
  onClose: () => void;
}

interface FormState {
  name: string;
  model: string;
  providerAlias: string;
  systemPrompt: string;
}

const EMPTY_FORM: FormState = {
  name: "",
  model: "",
  providerAlias: "",
  systemPrompt: "",
};

export default function CouncilConfigPanel({ open, onClose }: CouncilConfigPanelProps) {
  const [members, setMembers] = useState<CouncilMemberInfo[]>([]);
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [editing, setEditing] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    refresh();
  }, [open]);

  async function refresh() {
    const [memberData, providerData] = await Promise.all([
      listCouncilMembers(),
      listProviders(),
    ]);
    setMembers(memberData.members);
    setProviders(providerData.providers);
  }

  function handleNew() {
    setForm(EMPTY_FORM);
    setError(null);
    setEditing("__new__");
  }

  function handleEdit(m: CouncilMemberInfo) {
    setForm({
      name: m.name,
      model: m.model || "",
      providerAlias: m.providerAlias || "",
      systemPrompt: m.systemPrompt,
    });
    setError(null);
    setEditing(m.name);
  }

  function handleBack() {
    setEditing(null);
    setError(null);
  }

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const payload = {
        model: form.model || null,
        providerAlias: form.providerAlias || null,
        systemPrompt: form.systemPrompt,
      };

      if (editing === "__new__") {
        await createCouncilMember({ name: form.name, ...payload });
      } else {
        await updateCouncilMember(editing!, payload);
      }

      await refresh();
      setEditing(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(name: string) {
    try {
      await deleteCouncilMember(name);
      await refresh();
      if (editing === name) setEditing(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  }

  // ── Edit / New view ──
  if (editing) {
    const isNew = editing === "__new__";
    return (
      <Overlay open={open} onClose={onClose} title={isNew ? "Add Council Member" : `Edit: ${editing}`}>
        <div className="flex flex-col gap-3">
          <button onClick={handleBack} className="text-xs text-accent self-start">&larr; Back</button>

          <label className="text-xs text-text-muted">Name</label>
          <input
            type="text"
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "") })}
            disabled={!isNew}
            placeholder="e.g. analyst"
            className="w-full rounded bg-bg border border-border px-3 py-2 text-sm text-text disabled:opacity-50"
          />

          <label className="text-xs text-text-muted">Provider</label>
          <select
            value={form.providerAlias}
            onChange={(e) => setForm({ ...form, providerAlias: e.target.value })}
            className="w-full rounded bg-bg border border-border px-3 py-2 text-sm text-text"
          >
            <option value="">Default</option>
            {providers.map((p) => (
              <option key={p.alias} value={p.alias}>
                {p.alias} ({p.defaultModel || "no model"})
              </option>
            ))}
          </select>

          <label className="text-xs text-text-muted">Model override</label>
          <input
            type="text"
            value={form.model}
            onChange={(e) => setForm({ ...form, model: e.target.value })}
            placeholder="Leave blank to use provider default"
            className="w-full rounded bg-bg border border-border px-3 py-2 text-sm text-text"
          />

          <label className="text-xs text-text-muted">System prompt</label>
          <textarea
            value={form.systemPrompt}
            onChange={(e) => setForm({ ...form, systemPrompt: e.target.value })}
            rows={10}
            placeholder="Instructions for this council member..."
            className="w-full rounded bg-bg border border-border px-3 py-2 text-sm text-text resize-y"
          />

          {error && <p className="text-xs text-red-400">{error}</p>}

          <div className="flex gap-2 justify-end">
            <button onClick={handleBack} className="text-xs px-3 py-1.5 rounded bg-surface-alt text-text-muted hover:text-text">
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={saving || !form.name || !form.systemPrompt}
              className="text-xs px-3 py-1.5 rounded bg-accent text-white hover:bg-accent/80 disabled:opacity-50"
            >
              {saving ? "Saving..." : "Save"}
            </button>
          </div>
        </div>
      </Overlay>
    );
  }

  // ── List view ──
  return (
    <Overlay open={open} onClose={onClose} title="Council Members">
      <div className="flex flex-col gap-3">
        <p className="text-xs text-text-muted">
          {members.length} member{members.length !== 1 ? "s" : ""} — each receives your query in parallel
        </p>

        {members.map((m) => (
          <div
            key={m.name}
            className="flex items-center justify-between gap-2 p-2 rounded-lg bg-bg border border-border"
          >
            <div className="flex flex-col gap-0.5 min-w-0">
              <span className="text-sm font-medium text-text truncate">{m.name}</span>
              <span className="text-xs text-text-muted truncate">
                {m.providerAlias || "default"}{m.model ? ` / ${m.model}` : ""}
              </span>
            </div>
            <div className="flex gap-1.5 shrink-0">
              <button
                onClick={() => handleEdit(m)}
                className="text-xs text-text-muted hover:text-text px-2 py-1 rounded bg-surface-alt"
              >
                Edit
              </button>
              <button
                onClick={() => handleDelete(m.name)}
                className="text-xs text-text-muted hover:text-red-400 px-2 py-1 rounded bg-surface-alt"
              >
                Del
              </button>
            </div>
          </div>
        ))}

        <button
          onClick={handleNew}
          className="text-xs text-accent hover:text-accent/80 self-start mt-1"
        >
          + Add member
        </button>
      </div>
    </Overlay>
  );
}
