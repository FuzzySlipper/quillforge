import { useEffect, useMemo, useState } from "react";
import {
  cloneGameTemplate,
  deleteGameTemplate,
  getGameTemplate,
  getGameTemplateCatalog,
  saveGameTemplate,
  validateGameTemplate,
} from "../api";
import type {
  GameTemplate,
  GameTemplateAgentPlayerConfig,
  GameTemplateCatalogResponse,
  GameTemplateModuleOption,
  GameTemplateProviderOption,
  GameTemplateRandomNameBehavior,
  GameTemplateRuleOptionValue,
  GameTemplateRuleOptionValueKind,
  GameTemplateSetupFieldOption,
  GameTemplateSummary,
  GameTemplateValidationResult,
} from "../types";

interface GameTemplateEditorProps {
  templates: GameTemplateSummary[];
  selectedTemplateId: string;
  onSelectTemplate: (templateId: string) => void;
  onTemplatesChanged: (preferredTemplateId?: string) => Promise<void>;
}

const RANDOM_NAME_BEHAVIORS: GameTemplateRandomNameBehavior[] = [
  "UseFixedNameWhenProvided",
  "AlwaysRandomize",
  "NeverRandomize",
];

function seatIds(rosterSize: number): string[] {
  return Array.from({ length: Math.max(1, rosterSize) }, (_, index) => `seat-${index + 1}`);
}

function slugify(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "") || "custom-game";
}

function nextCustomTemplateId(templates: GameTemplateSummary[]): string {
  const existing = new Set(templates.map((template) => template.templateId));
  if (!existing.has("custom-game")) return "custom-game";

  let index = 2;
  while (existing.has(`custom-game-${index}`)) index++;
  return `custom-game-${index}`;
}

function defaultRuleValue(field: GameTemplateSetupFieldOption): GameTemplateRuleOptionValue {
  const kind = field.valueKind as GameTemplateRuleOptionValueKind;
  return {
    name: field.name,
    kind,
    stringValue: kind === "String" ? "" : null,
    intValue: kind === "Int" ? 0 : null,
    boolValue: kind === "Bool" ? false : null,
    participantIdValue: kind === "ParticipantId" ? "seat-1" : null,
    participantSetValue: kind === "ParticipantSet" ? ["seat-1"] : [],
  };
}

function defaultAgent(participantId: string, providerAlias: string): GameTemplateAgentPlayerConfig {
  return {
    participantId,
    providerAlias,
    modelOverride: null,
    characterPrompt: null,
    personality: null,
    fixedName: participantId,
    randomNameBehavior: "UseFixedNameWhenProvided",
  };
}

function createDefaultTemplate(
  catalog: GameTemplateCatalogResponse | null,
  preferredTemplateId = "custom-game",
): GameTemplate {
  const module = catalog?.modules[0] ?? null;
  const providerAlias = catalog?.providers[0]?.alias ?? "";
  const rosterSize = Math.max(module?.minimumPlayers ?? 4, 2);
  const userSeatParticipantId = "seat-1";
  const agents = seatIds(rosterSize)
    .filter((seatId) => seatId !== userSeatParticipantId)
    .map((seatId) => defaultAgent(seatId, providerAlias));

  return {
    templateId: preferredTemplateId,
    displayName: "Custom Game",
    description: null,
    module: {
      moduleId: module?.moduleId ?? "werewolf",
      minimumVersion: module?.moduleVersion ?? "0.1.0",
      maximumVersion: module?.moduleVersion ?? "0.1.0",
    },
    templateVersion: module?.minimumTemplateVersion ?? "1.0.0",
    rulesOptions: {
      values: module?.setupFields.map(defaultRuleValue) ?? [],
    },
    roster: {
      rosterSize,
      userSeatParticipantId,
      agentPlayers: agents,
    },
    memory: {
      tokenBudget: module?.memoryExpectations.suggestedSummaryTokenBudget || 1024,
    },
    communication: {
      publicChannelEnabled: module?.communicationCapabilities.allowsPublicChannelMessages ?? true,
      directMessagesEnabled: module?.communicationCapabilities.allowsDirectMessages ?? true,
      hostMessagesEnabled: true,
    },
    naming: {
      randomizeAgentNames: true,
      randomNameSet: null,
      randomSeed: null,
    },
  };
}

function normalizeRoster(
  template: GameTemplate,
  providerAlias: string,
  nextRosterSize = template.roster.rosterSize,
  nextUserSeat = template.roster.userSeatParticipantId ?? "seat-1",
): GameTemplate {
  const seats = seatIds(nextRosterSize);
  const userSeat = seats.includes(nextUserSeat) ? nextUserSeat : seats[0];
  const existingAgents = new Map(template.roster.agentPlayers.map((agent) => [agent.participantId, agent]));
  const agentPlayers = seats
    .filter((seatId) => seatId !== userSeat)
    .map((seatId) => existingAgents.get(seatId) ?? defaultAgent(seatId, providerAlias));

  return {
    ...template,
    roster: {
      ...template.roster,
      rosterSize: nextRosterSize,
      userSeatParticipantId: userSeat,
      agentPlayers,
    },
  };
}

function mergeModuleDefaults(
  template: GameTemplate,
  module: GameTemplateModuleOption,
  providerAlias: string,
): GameTemplate {
  const existingValues = new Map(template.rulesOptions.values.map((value) => [value.name, value]));
  const values = module.setupFields.map((field) => existingValues.get(field.name) ?? defaultRuleValue(field));
  const rosterSize = Math.min(Math.max(template.roster.rosterSize, module.minimumPlayers), module.maximumPlayers);
  const merged: GameTemplate = {
    ...template,
    module: {
      moduleId: module.moduleId,
      minimumVersion: module.moduleVersion,
      maximumVersion: module.moduleVersion,
    },
    templateVersion: template.templateVersion || module.minimumTemplateVersion,
    rulesOptions: { values },
    memory: {
      ...template.memory,
      tokenBudget: template.memory.tokenBudget || module.memoryExpectations.suggestedSummaryTokenBudget || 1024,
    },
    communication: {
      publicChannelEnabled: template.communication.publicChannelEnabled && module.communicationCapabilities.allowsPublicChannelMessages,
      directMessagesEnabled: template.communication.directMessagesEnabled && module.communicationCapabilities.allowsDirectMessages,
      hostMessagesEnabled: template.communication.hostMessagesEnabled,
    },
  };

  return normalizeRoster(merged, providerAlias, rosterSize);
}

function providerModel(provider: GameTemplateProviderOption): string | null {
  return provider.model ?? provider.defaultModel;
}

function providerLabel(provider: GameTemplateProviderOption): string {
  const providerModelName = providerModel(provider);
  const model = providerModelName ? ` · ${providerModelName}` : " · no provider model";
  return `${provider.alias} (${provider.type}${model})`;
}

function ValidationPanel({ validation }: { validation: GameTemplateValidationResult | null }) {
  if (!validation) {
    return null;
  }

  if (validation.isValid) {
    return (
      <div className="rounded-lg border border-success/40 bg-success-soft px-3 py-2 text-xs text-text">
        Validation passed. Template service reports no issues.
      </div>
    );
  }

  return (
    <div className="rounded-lg border border-danger-border bg-danger-soft px-3 py-3 text-xs text-danger-text">
      <div className="font-semibold">Validation issues from template service</div>
      <ul className="mt-2 flex flex-col gap-1">
        {validation.issues.map((issue, index) => (
          <li key={`${issue.code}:${issue.field ?? "root"}:${index}`}>
            <span className="font-medium">{issue.code}</span>
            {issue.field ? ` · ${issue.field}` : ""}
            {issue.source ? ` · ${issue.source}` : ""}: {issue.message}
          </li>
        ))}
      </ul>
    </div>
  );
}

export default function GameTemplateEditor({
  templates,
  selectedTemplateId,
  onSelectTemplate,
  onTemplatesChanged,
}: GameTemplateEditorProps) {
  const [catalog, setCatalog] = useState<GameTemplateCatalogResponse | null>(null);
  const [template, setTemplate] = useState<GameTemplate | null>(null);
  const [validation, setValidation] = useState<GameTemplateValidationResult | null>(null);
  const [cloneTargetId, setCloneTargetId] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const selectedModule = useMemo(() => {
    if (!catalog || !template) return null;
    return catalog.modules.find((module) => module.moduleId === template.module.moduleId
      && module.moduleVersion === template.module.minimumVersion)
      ?? catalog.modules.find((module) => module.moduleId === template.module.moduleId)
      ?? null;
  }, [catalog, template]);
  const defaultProviderAlias = catalog?.providers[0]?.alias ?? "";

  async function refreshCatalog(): Promise<GameTemplateCatalogResponse> {
    const nextCatalog = await getGameTemplateCatalog();
    setCatalog(nextCatalog);
    return nextCatalog;
  }

  useEffect(() => {
    let cancelled = false;

    async function loadCatalog() {
      try {
        const nextCatalog = await getGameTemplateCatalog();
        if (cancelled) return;
        setCatalog(nextCatalog);
        setTemplate((current) => current ?? (selectedTemplateId ? current : createDefaultTemplate(nextCatalog)));
        if (!selectedTemplateId) {
          setValidation(null);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load template catalog");
        }
      }
    }

    void loadCatalog();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function loadTemplate() {
      if (!selectedTemplateId) return;
      setBusy(true);
      setError(null);
      try {
        const response = await getGameTemplate(selectedTemplateId);
        if (cancelled) return;
        setTemplate(response.template);
        setValidation(response.validation);
        setCloneTargetId(`${response.template.templateId}-copy`);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load template");
        }
      } finally {
        if (!cancelled) setBusy(false);
      }
    }

    void loadTemplate();

    return () => {
      cancelled = true;
    };
  }, [selectedTemplateId]);

  function updateTemplate(mutator: (current: GameTemplate) => GameTemplate) {
    setTemplate((current) => {
      if (!current) return current;
      return mutator(current);
    });
    setValidation(null);
    setMessage(null);
  }

  function updateAgent(participantId: string, mutator: (agent: GameTemplateAgentPlayerConfig) => GameTemplateAgentPlayerConfig) {
    updateTemplate((current) => ({
      ...current,
      roster: {
        ...current.roster,
        agentPlayers: current.roster.agentPlayers.map((agent) => (
          agent.participantId === participantId ? mutator(agent) : agent
        )),
      },
    }));
  }

  function updateRule(field: GameTemplateSetupFieldOption, rawValue: string | boolean) {
    updateTemplate((current) => {
      const nextValue = current.rulesOptions.values.find((value) => value.name === field.name) ?? defaultRuleValue(field);
      const kind = field.valueKind as GameTemplateRuleOptionValueKind;
      const updated: GameTemplateRuleOptionValue = {
        ...nextValue,
        kind,
        stringValue: null,
        intValue: null,
        boolValue: null,
        participantIdValue: null,
        participantSetValue: [],
      };

      if (kind === "String") updated.stringValue = String(rawValue);
      if (kind === "Int") updated.intValue = Number(rawValue) || 0;
      if (kind === "Bool") updated.boolValue = Boolean(rawValue);
      if (kind === "ParticipantId") updated.participantIdValue = String(rawValue);
      if (kind === "ParticipantSet") {
        updated.participantSetValue = String(rawValue)
          .split(",")
          .map((item) => item.trim())
          .filter(Boolean);
      }

      const existingIndex = current.rulesOptions.values.findIndex((value) => value.name === field.name);
      const values = [...current.rulesOptions.values];
      if (existingIndex >= 0) {
        values[existingIndex] = updated;
      } else {
        values.push(updated);
      }
      return { ...current, rulesOptions: { values } };
    });
  }

  async function runOperation(operation: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await operation();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Template operation failed");
    } finally {
      setBusy(false);
    }
  }

  async function handleValidate() {
    if (!template) return;
    await runOperation(async () => {
      const response = await validateGameTemplate(template);
      setValidation(response.validation);
      setMessage(response.validation.isValid ? "Template validation passed." : "Template validation returned issues.");
    });
  }

  async function handleSave() {
    if (!template) return;
    await runOperation(async () => {
      const validated = await validateGameTemplate(template);
      setValidation(validated.validation);
      if (!validated.validation.isValid) {
        setMessage("Fix validation issues before saving.");
        return;
      }

      const response = await saveGameTemplate(template.templateId, template);
      setTemplate(response.template);
      setValidation(response.validation);
      onSelectTemplate(response.template.templateId);
      await onTemplatesChanged(response.template.templateId);
      await refreshCatalog();
      setMessage("Template saved.");
    });
  }

  async function handleClone() {
    if (!template || !selectedTemplateId || !cloneTargetId.trim()) return;
    await runOperation(async () => {
      const response = await cloneGameTemplate(selectedTemplateId, cloneTargetId.trim(), `${template.displayName} Copy`);
      setTemplate(response.template);
      setValidation(response.validation);
      onSelectTemplate(response.template.templateId);
      await onTemplatesChanged(response.template.templateId);
      await refreshCatalog();
      setMessage("Template cloned.");
    });
  }

  async function handleDelete() {
    if (!template || !selectedTemplateId) return;
    await runOperation(async () => {
      await deleteGameTemplate(selectedTemplateId);
      const nextId = templates.find((item) => item.templateId !== selectedTemplateId)?.templateId ?? "";
      onSelectTemplate(nextId);
      await onTemplatesChanged(nextId);
      const nextCatalog = await refreshCatalog();
      if (nextId) {
        const response = await getGameTemplate(nextId);
        setTemplate(response.template);
        setValidation(response.validation);
      } else {
        setTemplate(createDefaultTemplate(nextCatalog));
        setValidation(null);
      }
      setMessage("Template deleted.");
    });
  }

  function handleNew() {
    if (!catalog) return;
    const nextId = nextCustomTemplateId(templates);
    setTemplate(createDefaultTemplate(catalog, nextId));
    setValidation(null);
    setCloneTargetId(`${nextId}-copy`);
    onSelectTemplate("");
    setMessage("Editing a new unsaved template.");
  }

  if (!template) {
    return (
      <div className="qf-shell-card border-dashed px-4 py-4 text-sm text-text-muted">
        Loading template editor...
      </div>
    );
  }

  const seats = seatIds(template.roster.rosterSize);

  return (
    <div className="qf-shell-card flex flex-col gap-4 px-4 py-4" data-testid="game-template-editor">
      <div>
        <div className="qf-shell-folio">Template Editor</div>
        <p className="mt-2 text-sm leading-6 text-text-muted">
          Edit saved game templates through typed APIs. Rule legality and provider availability are reported by the template service.
        </p>
      </div>

      {error && <div role="alert" className="rounded-lg border border-danger-border bg-danger-soft px-3 py-2 text-xs text-danger-text">{error}</div>}
      {message && <div className="rounded-lg border border-border bg-surface-alt px-3 py-2 text-xs text-text-muted">{message}</div>}
      <ValidationPanel validation={validation} />

      <div className="grid gap-3 lg:grid-cols-[minmax(180px,0.8fr)_minmax(0,1fr)_auto]">
        <label className="flex flex-col gap-1 text-xs text-text-muted">
          Load template
          <select
            value={selectedTemplateId}
            onChange={(event) => onSelectTemplate(event.target.value)}
            className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
          >
            <option value="">New unsaved template</option>
            {templates.map((item) => (
              <option key={item.templateId} value={item.templateId}>{item.displayName}</option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs text-text-muted">
          Clone target id
          <input
            value={cloneTargetId}
            onChange={(event) => setCloneTargetId(event.target.value)}
            className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
          />
        </label>
        <div className="flex flex-wrap items-end gap-2">
          <button type="button" disabled={busy || !catalog} onClick={handleNew} className="rounded-lg bg-surface-alt px-3 py-2 text-xs text-text hover:bg-border disabled:opacity-50">New</button>
          <button type="button" disabled={busy || !selectedTemplateId || !cloneTargetId.trim()} onClick={() => { void handleClone(); }} className="rounded-lg bg-surface-alt px-3 py-2 text-xs text-text hover:bg-border disabled:opacity-50">Clone</button>
          <button type="button" disabled={busy || !selectedTemplateId} onClick={() => { void handleDelete(); }} className="rounded-lg bg-danger-strong px-3 py-2 text-xs text-white hover:bg-danger-strong-hover disabled:opacity-50">Delete</button>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <label className="flex flex-col gap-1 text-xs text-text-muted">
          Template id
          <input
            value={template.templateId}
            onChange={(event) => updateTemplate((current) => ({ ...current, templateId: event.target.value.trim() ? slugify(event.target.value) : "" }))}
            className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-text-muted">
          Display name
          <input
            value={template.displayName}
            onChange={(event) => updateTemplate((current) => ({ ...current, displayName: event.target.value }))}
            className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-text-muted md:col-span-2">
          Description
          <textarea
            value={template.description ?? ""}
            onChange={(event) => updateTemplate((current) => ({ ...current, description: event.target.value || null }))}
            rows={2}
            className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
          />
        </label>
      </div>

      <section className="rounded-xl border border-border/60 bg-surface/60 px-3 py-3">
        <div className="qf-shell-folio mb-3">Module & Rules</div>
        <div className="grid gap-3 md:grid-cols-3">
          <label className="flex flex-col gap-1 text-xs text-text-muted md:col-span-2">
            Module/game
            <select
              value={`${template.module.moduleId}:${template.module.minimumVersion}`}
              onChange={(event) => {
                const [moduleId, moduleVersion] = event.target.value.split(":", 2);
                const module = catalog?.modules.find((item) => item.moduleId === moduleId && item.moduleVersion === moduleVersion);
                if (module) updateTemplate((current) => mergeModuleDefaults(current, module, defaultProviderAlias));
              }}
              className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
            >
              {catalog?.modules.map((module) => (
                <option key={`${module.moduleId}:${module.moduleVersion}`} value={`${module.moduleId}:${module.moduleVersion}`}>
                  {module.displayName} · {module.moduleId}@{module.moduleVersion}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1 text-xs text-text-muted">
            Template version
            <input
              value={template.templateVersion}
              onChange={(event) => updateTemplate((current) => ({ ...current, templateVersion: event.target.value }))}
              className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
            />
          </label>
        </div>
        {selectedModule && (
          <div className="mt-2 text-xs leading-5 text-text-muted">
            Players {selectedModule.minimumPlayers}–{selectedModule.maximumPlayers}; template versions {selectedModule.minimumTemplateVersion}–{selectedModule.maximumTemplateVersion}.
          </div>
        )}
        <div className="mt-3 grid gap-3 md:grid-cols-2">
          {(selectedModule?.setupFields ?? []).map((field) => {
            const value = template.rulesOptions.values.find((item) => item.name === field.name) ?? defaultRuleValue(field);
            const raw = field.valueKind === "String" ? value.stringValue ?? ""
              : field.valueKind === "Int" ? String(value.intValue ?? 0)
                : field.valueKind === "Bool" ? value.boolValue ?? false
                  : field.valueKind === "ParticipantId" ? value.participantIdValue ?? ""
                    : value.participantSetValue.join(", ");
            return (
              <label key={field.name} className="flex flex-col gap-1 text-xs text-text-muted">
                <span>{field.displayName}{field.isRequired ? " *" : ""}</span>
                {field.valueKind === "Bool" ? (
                  <span className="flex items-center gap-2 rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text">
                    <input type="checkbox" checked={Boolean(raw)} onChange={(event) => updateRule(field, event.target.checked)} />
                    Enabled
                  </span>
                ) : (
                  <input
                    value={String(raw)}
                    type={field.valueKind === "Int" ? "number" : "text"}
                    onChange={(event) => updateRule(field, event.target.value)}
                    className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
                  />
                )}
                <span className="text-[11px] leading-4 text-text-muted">{field.description}</span>
              </label>
            );
          })}
        </div>
      </section>

      <section className="rounded-xl border border-border/60 bg-surface/60 px-3 py-3">
        <div className="qf-shell-folio mb-3">Roster, Memory & Communication</div>
        <div className="grid gap-3 md:grid-cols-4">
          <label className="flex flex-col gap-1 text-xs text-text-muted">
            Roster size
            <input
              type="number"
              min={selectedModule?.minimumPlayers ?? 1}
              max={selectedModule?.maximumPlayers ?? 99}
              value={template.roster.rosterSize}
              onChange={(event) => updateTemplate((current) => normalizeRoster(current, defaultProviderAlias, Number(event.target.value) || 1))}
              className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
            />
          </label>
          <label className="flex flex-col gap-1 text-xs text-text-muted">
            User seat
            <select
              value={template.roster.userSeatParticipantId ?? ""}
              onChange={(event) => updateTemplate((current) => normalizeRoster(current, defaultProviderAlias, current.roster.rosterSize, event.target.value))}
              className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
            >
              {seats.map((seatId) => <option key={seatId} value={seatId}>{seatId}</option>)}
            </select>
          </label>
          <label className="flex flex-col gap-1 text-xs text-text-muted">
            Memory token budget
            <input
              type="number"
              min={0}
              value={template.memory.tokenBudget}
              onChange={(event) => updateTemplate((current) => ({ ...current, memory: { tokenBudget: Number(event.target.value) || 0 } }))}
              className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
            />
          </label>
          <label className="flex flex-col gap-1 text-xs text-text-muted">
            Random seed
            <input
              type="number"
              value={template.naming.randomSeed ?? ""}
              onChange={(event) => updateTemplate((current) => ({ ...current, naming: { ...current.naming, randomSeed: event.target.value ? Number(event.target.value) : null } }))}
              className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
            />
          </label>
        </div>
        {selectedModule?.memoryExpectations.usesRoundSummaries && (
          <div className="mt-2 text-xs text-text-muted">
            Module suggests {selectedModule.memoryExpectations.suggestedSummaryTokenBudget} memory-summary tokens and keeps up to {selectedModule.memoryExpectations.maximumRetainedRoundSummaries} summaries.
          </div>
        )}
        <div className="mt-3 grid gap-2 md:grid-cols-3">
          <label className="flex items-center gap-2 text-xs text-text-muted">
            <input type="checkbox" checked={template.communication.publicChannelEnabled} onChange={(event) => updateTemplate((current) => ({ ...current, communication: { ...current.communication, publicChannelEnabled: event.target.checked } }))} />
            Public channel {selectedModule?.communicationCapabilities.allowsPublicChannelMessages ? "" : "(module disallows)"}
          </label>
          <label className="flex items-center gap-2 text-xs text-text-muted">
            <input type="checkbox" checked={template.communication.directMessagesEnabled} onChange={(event) => updateTemplate((current) => ({ ...current, communication: { ...current.communication, directMessagesEnabled: event.target.checked } }))} />
            Direct messages {selectedModule?.communicationCapabilities.allowsDirectMessages ? "" : "(module disallows)"}
          </label>
          <label className="flex items-center gap-2 text-xs text-text-muted">
            <input type="checkbox" checked={template.communication.hostMessagesEnabled} onChange={(event) => updateTemplate((current) => ({ ...current, communication: { ...current.communication, hostMessagesEnabled: event.target.checked } }))} />
            Host messages
          </label>
          <label className="flex items-center gap-2 text-xs text-text-muted">
            <input type="checkbox" checked={template.naming.randomizeAgentNames} onChange={(event) => updateTemplate((current) => ({ ...current, naming: { ...current.naming, randomizeAgentNames: event.target.checked } }))} />
            Randomize agent names
          </label>
          <label className="flex flex-col gap-1 text-xs text-text-muted md:col-span-2">
            Random name set
            <input
              value={template.naming.randomNameSet ?? ""}
              onChange={(event) => updateTemplate((current) => ({ ...current, naming: { ...current.naming, randomNameSet: event.target.value || null } }))}
              className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
            />
          </label>
        </div>
      </section>

      <section className="rounded-xl border border-border/60 bg-surface/60 px-3 py-3">
        <div className="qf-shell-folio mb-3">Agent Seats</div>
        {catalog?.providers.length === 0 && (
          <div className="mb-3 rounded-lg border border-warning/40 bg-warning-soft px-3 py-2 text-xs text-warning-text">
            No providers are configured. Add providers in Provider settings before validating or saving agent seats.
          </div>
        )}
        <div className="flex flex-col gap-3">
          {template.roster.agentPlayers.map((agent) => {
            const provider = catalog?.providers.find((item) => item.alias === agent.providerAlias) ?? null;
            return (
              <div key={agent.participantId} className="qf-shell-card grid gap-3 px-3 py-3 md:grid-cols-2">
                <div className="md:col-span-2 flex items-center justify-between gap-3">
                  <div>
                    <div className="text-sm font-medium text-text">{agent.participantId}</div>
                    <div className="text-xs text-text-muted">Provider model: {provider ? (providerModel(provider) ?? "none") : "none"}</div>
                  </div>
                  <select
                    value={agent.randomNameBehavior}
                    onChange={(event) => updateAgent(agent.participantId, (current) => ({ ...current, randomNameBehavior: event.target.value as GameTemplateRandomNameBehavior }))}
                    className="rounded-lg border border-border bg-input-bg px-2 py-1 text-xs text-text"
                  >
                    {RANDOM_NAME_BEHAVIORS.map((behavior) => <option key={behavior} value={behavior}>{behavior}</option>)}
                  </select>
                </div>
                <label className="flex flex-col gap-1 text-xs text-text-muted">
                  Provider alias
                  <select
                    value={agent.providerAlias}
                    onChange={(event) => updateAgent(agent.participantId, (current) => ({ ...current, providerAlias: event.target.value }))}
                    className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
                  >
                    <option value="">Select provider...</option>
                    {catalog?.providers.map((item) => <option key={item.alias} value={item.alias}>{providerLabel(item)}</option>)}
                  </select>
                </label>
                <label className="flex flex-col gap-1 text-xs text-text-muted">
                  Model override
                  <input
                    value={agent.modelOverride ?? ""}
                    onChange={(event) => updateAgent(agent.participantId, (current) => ({ ...current, modelOverride: event.target.value || null }))}
                    placeholder={provider ? (providerModel(provider) ?? "use provider model") : "use provider model"}
                    className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
                  />
                </label>
                <label className="flex flex-col gap-1 text-xs text-text-muted">
                  Fixed name
                  <input
                    value={agent.fixedName ?? ""}
                    onChange={(event) => updateAgent(agent.participantId, (current) => ({ ...current, fixedName: event.target.value || null }))}
                    className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
                  />
                </label>
                <label className="flex flex-col gap-1 text-xs text-text-muted">
                  Personality
                  <input
                    value={agent.personality ?? ""}
                    onChange={(event) => updateAgent(agent.participantId, (current) => ({ ...current, personality: event.target.value || null }))}
                    className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
                  />
                </label>
                <label className="flex flex-col gap-1 text-xs text-text-muted md:col-span-2">
                  Character/personality prompt
                  <textarea
                    value={agent.characterPrompt ?? ""}
                    onChange={(event) => updateAgent(agent.participantId, (current) => ({ ...current, characterPrompt: event.target.value || null }))}
                    rows={2}
                    className="rounded-lg border border-border bg-input-bg px-3 py-2 text-sm text-text"
                  />
                </label>
              </div>
            );
          })}
        </div>
      </section>

      <div className="flex flex-wrap gap-2">
        <button type="button" disabled={busy} onClick={() => { void handleValidate(); }} className="rounded-lg bg-surface-alt px-4 py-2 text-sm text-text hover:bg-border disabled:opacity-50">Validate</button>
        <button type="button" disabled={busy} onClick={() => { void handleSave(); }} className="rounded-lg bg-accent px-4 py-2 text-sm font-medium text-accent-contrast hover:bg-accent-hover disabled:opacity-50">Save template</button>
      </div>
    </div>
  );
}
