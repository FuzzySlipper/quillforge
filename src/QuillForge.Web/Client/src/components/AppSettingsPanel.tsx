import { useEffect, useMemo, useState } from "react";
import {
  getAppSettings,
  updateWebSearchSettings,
  type WebSearchSettings,
  type WebSearchSettingsUpdate,
} from "../api";
import Overlay from "./Overlay";

interface AppSettingsPanelProps {
  open: boolean;
  onClose: () => void;
}

type SecretField = "tavily" | "brave" | "google" | "zai";

interface WebSearchFormState {
  enabled: boolean;
  provider: string;
  searxngUrl: string;
  tavilyApiKey: string;
  clearTavilyApiKey: boolean;
  braveApiKey: string;
  clearBraveApiKey: boolean;
  googleApiKey: string;
  clearGoogleApiKey: boolean;
  googleCxId: string;
  zaiApiKey: string;
  clearZaiApiKey: boolean;
  zaiMcpEndpoint: string;
  zaiMcpToolName: string;
  maxResults: string;
}

const PROVIDER_LABELS: Record<string, string> = {
  searxng: "SearXNG",
  tavily: "Tavily",
  brave: "Brave Search",
  google: "Google Custom Search",
  zai: "Z.AI Web Search",
};

function formFromSettings(settings: WebSearchSettings): WebSearchFormState {
  return {
    enabled: settings.enabled,
    provider: settings.provider || "searxng",
    searxngUrl: settings.searxngUrl ?? "",
    tavilyApiKey: "",
    clearTavilyApiKey: false,
    braveApiKey: "",
    clearBraveApiKey: false,
    googleApiKey: "",
    clearGoogleApiKey: false,
    googleCxId: settings.googleCxId ?? "",
    zaiApiKey: "",
    clearZaiApiKey: false,
    zaiMcpEndpoint: settings.zaiMcpEndpoint ?? "",
    zaiMcpToolName: settings.zaiMcpToolName ?? "",
    maxResults: String(settings.maxResults ?? 50),
  };
}

function normalizedText(value: string): string {
  return value.trim();
}

function secretPlaceholder(isSet: boolean, label: string): string {
  return isSet ? `${label} key is saved — leave blank to keep it` : `Paste ${label} key`;
}

export default function AppSettingsPanel({ open, onClose }: AppSettingsPanelProps) {
  const [settings, setSettings] = useState<WebSearchSettings | null>(null);
  const [form, setForm] = useState<WebSearchFormState | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;

    setLoading(true);
    setError(null);
    setNotice(null);
    getAppSettings()
      .then((data) => {
        setSettings(data.webSearch);
        setForm(formFromSettings(data.webSearch));
      })
      .catch((err) => setError(err instanceof Error ? err.message : "Failed to load settings"))
      .finally(() => setLoading(false));
  }, [open]);

  const providerOptions = useMemo(() => {
    const supported = settings?.supportedProviders?.length ? settings.supportedProviders : ["searxng", "tavily", "brave", "google", "zai"];
    return supported.map((provider) => ({
      provider,
      label: PROVIDER_LABELS[provider] ?? provider,
    }));
  }, [settings]);

  function updateForm(update: Partial<WebSearchFormState>) {
    setForm((current) => current ? { ...current, ...update } : current);
  }

  function setSecretClear(field: SecretField, clear: boolean) {
    if (field === "tavily") updateForm({ clearTavilyApiKey: clear, tavilyApiKey: clear ? "" : form?.tavilyApiKey ?? "" });
    if (field === "brave") updateForm({ clearBraveApiKey: clear, braveApiKey: clear ? "" : form?.braveApiKey ?? "" });
    if (field === "google") updateForm({ clearGoogleApiKey: clear, googleApiKey: clear ? "" : form?.googleApiKey ?? "" });
    if (field === "zai") updateForm({ clearZaiApiKey: clear, zaiApiKey: clear ? "" : form?.zaiApiKey ?? "" });
  }

  async function handleSave() {
    if (!form) return;

    setSaving(true);
    setError(null);
    setNotice(null);

    const maxResults = Number.parseInt(form.maxResults, 10);
    if (!Number.isFinite(maxResults) || maxResults < 1 || maxResults > 100) {
      setSaving(false);
      setError("Max results must be between 1 and 100.");
      return;
    }

    const payload: WebSearchSettingsUpdate = {
      enabled: form.enabled,
      provider: form.provider,
      searxngUrl: normalizedText(form.searxngUrl),
      googleCxId: normalizedText(form.googleCxId),
      zaiMcpEndpoint: normalizedText(form.zaiMcpEndpoint),
      zaiMcpToolName: normalizedText(form.zaiMcpToolName),
      maxResults,
      clearTavilyApiKey: form.clearTavilyApiKey,
      clearBraveApiKey: form.clearBraveApiKey,
      clearGoogleApiKey: form.clearGoogleApiKey,
      clearZaiApiKey: form.clearZaiApiKey,
    };

    if (form.tavilyApiKey.trim()) payload.tavilyApiKey = form.tavilyApiKey.trim();
    if (form.braveApiKey.trim()) payload.braveApiKey = form.braveApiKey.trim();
    if (form.googleApiKey.trim()) payload.googleApiKey = form.googleApiKey.trim();
    if (form.zaiApiKey.trim()) payload.zaiApiKey = form.zaiApiKey.trim();

    try {
      const saved = await updateWebSearchSettings(payload);
      setSettings(saved.webSearch);
      setForm(formFromSettings(saved.webSearch));
      setNotice("Web search settings saved. New web_search calls will use the updated settings.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save settings");
    } finally {
      setSaving(false);
    }
  }

  const inputClass = "w-full bg-input-bg text-text border border-border rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-accent";
  const helpClass = "text-[11px] text-text-muted mt-1";

  function renderSecretField(
    label: string,
    value: string,
    clear: boolean,
    isSet: boolean,
    placeholderLabel: string,
    onValue: (value: string) => void,
    onClear: (clear: boolean) => void,
  ) {
    return (
      <div>
        <label className="text-xs text-text-muted uppercase tracking-wider">{label}</label>
        <input
          type="password"
          value={value}
          disabled={clear}
          onChange={(e) => onValue(e.target.value)}
          placeholder={secretPlaceholder(isSet, placeholderLabel)}
          className={inputClass}
        />
        {isSet && (
          <label className="mt-1 flex items-center gap-2 text-[11px] text-text-muted">
            <input
              type="checkbox"
              checked={clear}
              onChange={(e) => onClear(e.target.checked)}
              className=""
            />
            Clear saved key on save
          </label>
        )}
      </div>
    );
  }

  function renderProviderFields() {
    if (!form || !settings) return null;

    if (form.provider === "searxng") {
      return (
        <div>
          <label className="text-xs text-text-muted uppercase tracking-wider">SearXNG URL</label>
          <input
            type="url"
            value={form.searxngUrl}
            onChange={(e) => updateForm({ searxngUrl: e.target.value })}
            placeholder="http://localhost:8080"
            className={inputClass}
          />
          <p className={helpClass}>Use the base URL for your self-hosted SearXNG instance.</p>
        </div>
      );
    }

    if (form.provider === "tavily") {
      return renderSecretField(
        "Tavily API Key",
        form.tavilyApiKey,
        form.clearTavilyApiKey,
        settings.tavilyApiKeySet,
        "Tavily",
        (value) => updateForm({ tavilyApiKey: value }),
        (clear) => setSecretClear("tavily", clear),
      );
    }

    if (form.provider === "brave") {
      return renderSecretField(
        "Brave API Key",
        form.braveApiKey,
        form.clearBraveApiKey,
        settings.braveApiKeySet,
        "Brave",
        (value) => updateForm({ braveApiKey: value }),
        (clear) => setSecretClear("brave", clear),
      );
    }

    if (form.provider === "google") {
      return (
        <>
          {renderSecretField(
            "Google API Key",
            form.googleApiKey,
            form.clearGoogleApiKey,
            settings.googleApiKeySet,
            "Google",
            (value) => updateForm({ googleApiKey: value }),
            (clear) => setSecretClear("google", clear),
          )}
          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Google CX ID</label>
            <input
              type="text"
              value={form.googleCxId}
              onChange={(e) => updateForm({ googleCxId: e.target.value })}
              placeholder="Custom Search Engine ID"
              className={inputClass}
            />
          </div>
        </>
      );
    }

    if (form.provider === "zai") {
      return (
        <>
          {renderSecretField(
            "Z.AI API Key",
            form.zaiApiKey,
            form.clearZaiApiKey,
            settings.zaiApiKeySet,
            "Z.AI",
            (value) => updateForm({ zaiApiKey: value }),
            (clear) => setSecretClear("zai", clear),
          )}
          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Z.AI MCP Endpoint</label>
            <input
              type="url"
              value={form.zaiMcpEndpoint}
              onChange={(e) => updateForm({ zaiMcpEndpoint: e.target.value })}
              placeholder="https://api.z.ai/api/mcp/web_search_prime/mcp"
              className={inputClass}
            />
            <p className={helpClass}>Optional. Leave blank to use Z.AI's documented Web Search MCP endpoint.</p>
          </div>
          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Z.AI MCP Tool Name</label>
            <input
              type="text"
              value={form.zaiMcpToolName}
              onChange={(e) => updateForm({ zaiMcpToolName: e.target.value })}
              placeholder="webSearchPrime"
              className={inputClass}
            />
          </div>
        </>
      );
    }

    return null;
  }

  return (
    <Overlay open={open} onClose={onClose} title="App Settings">
      {loading && <p className="text-sm text-text-muted">Loading settings…</p>}
      {!loading && form && (
        <div className="flex flex-col gap-4">
          <div>
            <div className="text-xs text-text-muted uppercase tracking-wider mb-2">Web Search</div>
            <div className="rounded-lg border border-border/70 bg-input-bg/30 px-3 py-3 text-xs text-text-muted">
              Configure the `web_search` tool without editing config.yaml. API keys are write-only here; saved keys are shown as status instead of being revealed.
            </div>
          </div>

          <label className="flex items-start gap-3 rounded-lg border border-border/70 bg-input-bg/30 px-3 py-3">
            <input
              type="checkbox"
              checked={form.enabled}
              onChange={(e) => updateForm({ enabled: e.target.checked })}
              className="mt-0.5"
            />
            <span>
              <span className="block text-sm font-medium text-text">Enable web search</span>
              <span className="block text-xs text-text-muted">Allows research and lore-building agents to call real-world search when the active mode permits it.</span>
            </span>
          </label>

          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Provider</label>
            <select
              value={form.provider}
              onChange={(e) => updateForm({ provider: e.target.value })}
              className={inputClass}
            >
              {providerOptions.map((option) => (
                <option key={option.provider} value={option.provider}>{option.label}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="text-xs text-text-muted uppercase tracking-wider">Max Results</label>
            <input
              type="number"
              min={1}
              max={100}
              step={1}
              value={form.maxResults}
              onChange={(e) => updateForm({ maxResults: e.target.value })}
              className={inputClass}
            />
            <p className={helpClass}>Provider-specific caps may still apply; Brave, for example, clamps to its own result limit.</p>
          </div>

          <div className="flex flex-col gap-3 rounded-lg border border-border/70 bg-surface-alt/30 px-3 py-3">
            <div className="text-xs text-text-muted uppercase tracking-wider">
              {PROVIDER_LABELS[form.provider] ?? form.provider} Settings
            </div>
            {renderProviderFields()}
          </div>

          {notice && <p className="text-xs text-success px-1">{notice}</p>}
          {error && <p className="text-xs text-danger px-1">{error}</p>}

          <div className="flex justify-end gap-2 pt-1">
            <button onClick={onClose} className="text-sm text-text-muted hover:text-text px-3 py-1.5">
              Close
            </button>
            <button
              onClick={handleSave}
              disabled={saving}
              className="text-sm bg-accent text-accent-contrast rounded-lg px-4 py-1.5 disabled:opacity-50"
            >
              {saving ? "Saving…" : "Save Settings"}
            </button>
          </div>
        </div>
      )}
    </Overlay>
  );
}
