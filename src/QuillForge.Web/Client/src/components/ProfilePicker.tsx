import { useEffect, useState } from "react";
import Overlay from "./Overlay";
import { getProfiles, switchProfile } from "../api";
import type { Profiles } from "../types";

interface ProfilePickerProps {
  open: boolean;
  onClose: () => void;
  onSwitched: (sessionId?: string | null) => void;
  sessionId?: string | null;
}

export default function ProfilePicker({ open, onClose, onSwitched, sessionId }: ProfilePickerProps) {
  const [profiles, setProfiles] = useState<Profiles | null>(null);
  const [conductor, setConductor] = useState("");
  const [loreSet, setLoreSet] = useState("");
  const [narrativeRules, setNarrativeRules] = useState("");
  const [style, setStyle] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    getProfiles(sessionId).then((p) => {
      setProfiles(p);
      setConductor(p.activeConductor);
      setLoreSet(p.activeLore);
      setNarrativeRules(p.activeNarrativeRules);
      setStyle(p.activeWritingStyle);
    });
  }, [open, sessionId]);

  async function handleSave() {
    setSaving(true);
    try {
      const result = await switchProfile({
        sessionId,
        conductor,
        lore: loreSet,
        narrativeRules,
        writingStyle: style,
      });
      onSwitched(result.sessionId ?? sessionId);
      onClose();
    } finally {
      setSaving(false);
    }
  }

  return (
    <Overlay open={open} onClose={onClose} title="Profile">
      {profiles ? (
        <div className="flex flex-col gap-4">
          <label className="flex flex-col gap-1">
            <span className="text-sm text-text-muted">Conductor</span>
            <select
              value={conductor}
              onChange={(e) => setConductor(e.target.value)}
              className="bg-input-bg text-text border border-border rounded-lg px-3 py-2"
            >
              {profiles.conductors.map((p) => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-text-muted">Lore Set</span>
            <select
              value={loreSet}
              onChange={(e) => setLoreSet(e.target.value)}
              className="bg-input-bg text-text border border-border rounded-lg px-3 py-2"
            >
              {profiles.loreSets.map((l) => (
                <option key={l} value={l}>{l}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-text-muted">Narrative Rules</span>
            <select
              value={narrativeRules}
              onChange={(e) => setNarrativeRules(e.target.value)}
              className="bg-input-bg text-text border border-border rounded-lg px-3 py-2"
            >
              {profiles.narrativeRules.map((rules) => (
                <option key={rules} value={rules}>{rules}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-text-muted">Writing Style</span>
            <select
              value={style}
              onChange={(e) => setStyle(e.target.value)}
              className="bg-input-bg text-text border border-border rounded-lg px-3 py-2"
            >
              {profiles.writingStyles.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
          </label>

          <button
            onClick={handleSave}
            disabled={saving}
            className="mt-2 bg-accent hover:bg-accent-hover text-white font-semibold rounded-lg px-4 py-2.5 disabled:opacity-50 transition-colors"
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
