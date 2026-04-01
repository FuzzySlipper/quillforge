import type { Status, Profiles, ModeInfo, Mode } from "./types";

const BASE = "";

async function request<T>(
  path: string,
  opts?: RequestInit,
): Promise<T> {
  const headers: Record<string, string> = {};
  // Only set Content-Type for requests with a body
  if (opts?.body) headers["Content-Type"] = "application/json";
  const res = await fetch(`${BASE}${path}`, {
    headers,
    ...opts,
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`${res.status}: ${body}`);
  }
  return res.json();
}

export async function getStatus(sessionId?: string | null): Promise<Status> {
  const query = sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : "";
  return request(`/api/status${query}`);
}

export async function sendChat(
  message: string,
): Promise<{ content: string; responseType: string }> {
  return request("/api/chat", {
    method: "POST",
    body: JSON.stringify({ message }),
  });
}

export interface StreamEvent {
  type: "status" | "tool" | "done" | "error" | "text_delta" | "reasoning_delta" | "diagnostic" | "persisted";
  data: Record<string, unknown>;
}

export async function sendChatStream(
  message: string,
  onEvent: (event: StreamEvent) => void,
  signal?: AbortSignal,
  sessionId?: string | null,
  parentId?: string | null,
): Promise<void> {
  const payload: Record<string, unknown> = { message };
  if (sessionId) payload.sessionId = sessionId;
  if (parentId) payload.parentId = parentId;
  const res = await fetch("/api/chat/stream", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
    signal,
  });

  if (!res.ok) {
    throw new Error(`${res.status}: ${await res.text()}`);
  }

  const reader = res.body?.getReader();
  if (!reader) throw new Error("No response body");

  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // Parse SSE events from buffer
    const lines = buffer.split("\n");
    buffer = lines.pop() || "";

    let currentEvent = "status";
    for (const line of lines) {
      if (line.startsWith("event: ")) {
        currentEvent = line.slice(7).trim();
      } else if (line.startsWith("data: ")) {
        try {
          const data = JSON.parse(line.slice(6));
          const type = (data.type ?? currentEvent) as StreamEvent["type"];
          onEvent({ type, data });
        } catch {
          // Skip malformed data
        }
      }
    }
  }
}

export async function sendCouncilStream(
  query: string,
  onEvent: (event: StreamEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch("/api/council", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query }),
    signal,
  });

  if (!res.ok) {
    throw new Error(`${res.status}: ${await res.text()}`);
  }

  const reader = res.body?.getReader();
  if (!reader) throw new Error("No response body");

  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop() || "";

    let currentEvent = "status";
    for (const line of lines) {
      if (line.startsWith("event: ")) {
        currentEvent = line.slice(7).trim();
      } else if (line.startsWith("data: ")) {
        try {
          const data = JSON.parse(line.slice(6));
          const type = (data.type ?? currentEvent) as StreamEvent["type"];
          onEvent({ type, data });
        } catch {
          // Skip malformed data
        }
      }
    }
  }
}

export async function sendProbeStream(
  onEvent: (event: StreamEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch("/api/probe", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: "{}",
    signal,
  });

  if (!res.ok) {
    throw new Error(`${res.status}: ${await res.text()}`);
  }

  const reader = res.body?.getReader();
  if (!reader) throw new Error("No response body");

  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop() || "";

    let currentEvent = "status";
    for (const line of lines) {
      if (line.startsWith("event: ")) {
        currentEvent = line.slice(7).trim();
      } else if (line.startsWith("data: ")) {
        try {
          const data = JSON.parse(line.slice(6));
          const type = (data.type ?? currentEvent) as StreamEvent["type"];
          onEvent({ type, data });
        } catch {
          // Skip malformed data
        }
      }
    }
  }
}

// ── Council member management ──

export interface CouncilMemberInfo {
  name: string;
  model: string | null;
  providerAlias: string | null;
  systemPrompt: string;
}

export async function listCouncilMembers(): Promise<{ members: CouncilMemberInfo[] }> {
  return request("/api/council/members");
}

export async function createCouncilMember(member: {
  name: string;
  model?: string | null;
  providerAlias?: string | null;
  systemPrompt: string;
}): Promise<unknown> {
  return request("/api/council/members", {
    method: "POST",
    body: JSON.stringify(member),
  });
}

export async function updateCouncilMember(
  name: string,
  updates: { model?: string | null; providerAlias?: string | null; systemPrompt?: string },
): Promise<unknown> {
  return request(`/api/council/members/${encodeURIComponent(name)}`, {
    method: "PUT",
    body: JSON.stringify(updates),
  });
}

export async function deleteCouncilMember(name: string): Promise<unknown> {
  return request(`/api/council/members/${encodeURIComponent(name)}`, {
    method: "DELETE",
  });
}

export async function requestImage(
  prompt: string,
): Promise<{ status: string; imageUrl?: string; error?: string; prompt: string }> {
  return request("/api/imagine", {
    method: "POST",
    body: JSON.stringify({ prompt }),
  });
}

export async function getProfiles(): Promise<Profiles> {
  return request("/api/profiles");
}

export async function switchProfile(profile: {
  persona?: string;
  lore?: string;
  writingStyle?: string;
}): Promise<unknown> {
  return request("/api/profiles/switch", {
    method: "POST",
    body: JSON.stringify(profile),
  });
}

export async function getMode(sessionId?: string | null): Promise<ModeInfo> {
  const query = sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : "";
  return request(`/api/mode${query}`);
}

export async function setMode(
  mode: Mode,
  project?: string,
  file?: string,
  character?: string,
  sessionId?: string | null,
): Promise<unknown> {
  return request("/api/mode", {
    method: "POST",
    body: JSON.stringify({ mode, project, file, character, sessionId }),
  });
}

export async function getProjects(mode?: string): Promise<{ projects: string[] }> {
  const query = mode ? `?mode=${encodeURIComponent(mode)}` : "";
  return request(`/api/projects${query}`);
}

export async function newSession(): Promise<{ sessionId: string; name: string }> {
  return request("/api/session/new", { method: "POST" });
}

// ── Session management ──

export interface SessionInfo {
  id: string;
  name: string;
  lastMode: string | null;
  messageCount: number;
  updatedAt: string;
}

export async function listSessions(): Promise<{ sessions: SessionInfo[] }> {
  return request("/api/sessions");
}

export async function loadSession(
  id: string,
): Promise<{
  sessionId: string;
  name: string;
  messages: Array<{
    id: string;
    role: string;
    content: string;
    createdAt: string;
    parentId?: string | null;
    variants?: Array<{ content: string; createdAt: string }> | null;
  }>;
}> {
  return request(`/api/sessions/${encodeURIComponent(id)}/load`, { method: "POST" });
}

export async function deleteSession(id: string): Promise<unknown> {
  return request(`/api/sessions/${encodeURIComponent(id)}`, { method: "DELETE" });
}

// ── Conversation manipulation (GUID-based, session-scoped) ──

export async function conversationDeleteMessage(
  sessionId: string,
  messageId: string,
): Promise<{ removed: number }> {
  return request(`/api/sessions/${encodeURIComponent(sessionId)}/messages/${encodeURIComponent(messageId)}`, {
    method: "DELETE",
  });
}

export async function conversationFork(
  sessionId: string,
  messageId?: string,
): Promise<{ sessionId: string; name: string; messageCount: number }> {
  return request(`/api/sessions/${encodeURIComponent(sessionId)}/fork`, {
    method: "POST",
    body: JSON.stringify({ messageId }),
  });
}

export async function conversationRegenerate(
  sessionId: string,
  messageId: string,
): Promise<{ parentId: string; sessionId: string }> {
  return request(`/api/sessions/${encodeURIComponent(sessionId)}/messages/${encodeURIComponent(messageId)}/regenerate`, {
    method: "POST",
  });
}

export interface LoreFileInfo {
  path: string;
  tokens: number;
  size: number;
}

export async function listLore(): Promise<{
  files: LoreFileInfo[];
  categories: string[];
  activeProject: string | null;
  lorePath: string;
}> {
  return request("/api/lore");
}

export async function readLore(path: string): Promise<{ path: string; content: string; tokens: number }> {
  return request(`/api/lore/${path.split("/").map(encodeURIComponent).join("/")}`);
}

export async function writeLore(path: string, content: string): Promise<unknown> {
  return request(`/api/lore/${path.split("/").map(encodeURIComponent).join("/")}`, {
    method: "PUT",
    body: JSON.stringify({ content }),
  });
}

export async function deleteLore(path: string): Promise<unknown> {
  return request(`/api/lore/${path.split("/").map(encodeURIComponent).join("/")}`, { method: "DELETE" });
}

export async function listLoreProjects(): Promise<{ projects: string[]; active: string }> {
  return request("/api/lore/projects");
}

export async function createLoreProject(name: string): Promise<{ status: string; name: string }> {
  return request("/api/lore/projects", {
    method: "POST",
    body: JSON.stringify({ name }),
  });
}

// ── Persona / prompt files ──

export interface PersonaFileInfo {
  path: string;
  tokens: number;
  size: number;
}

export async function listPersona(): Promise<{ files: PersonaFileInfo[]; personaPath: string }> {
  return request("/api/persona");
}

export async function readPersona(path: string): Promise<{ path: string; content: string; tokens: number }> {
  return request(`/api/persona/${path.split("/").map(encodeURIComponent).join("/")}`);
}

export async function writePersona(path: string, content: string): Promise<unknown> {
  return request(`/api/persona/${path.split("/").map(encodeURIComponent).join("/")}`, {
    method: "PUT",
    body: JSON.stringify({ content }),
  });
}

// ── Writing styles ──

export interface WritingStyleInfo {
  path: string;
  name: string;
  tokens: number;
  size: number;
}

export async function listWritingStyles(): Promise<{ files: WritingStyleInfo[]; active: string }> {
  return request("/api/writing-styles");
}

export async function readWritingStyle(name: string): Promise<{ name: string; content: string; tokens: number }> {
  return request(`/api/writing-styles/${encodeURIComponent(name)}`);
}

export async function writeWritingStyle(name: string, content: string): Promise<unknown> {
  return request(`/api/writing-styles/${encodeURIComponent(name)}`, {
    method: "PUT",
    body: JSON.stringify({ content }),
  });
}

// ── Character cards ──

export interface CharacterCardSummary {
  fileName: string;
  name: string;
  portrait: string | null;
}

export interface CharacterCard {
  name: string;
  portrait: string;
  personality: string;
  description: string;
  scenario: string;
  greeting: string;
  _filename?: string;
}

export async function listCharacterCards(): Promise<{
  cards: CharacterCardSummary[];
  activeAi: string | null;
  activeUser: string | null;
}> {
  return request("/api/character-cards");
}

export async function readCharacterCard(name: string): Promise<CharacterCard> {
  return request(`/api/character-cards/${encodeURIComponent(name)}`);
}

export async function createCharacterCard(card: Omit<CharacterCard, "_filename">): Promise<{ status: string; filename: string }> {
  return request("/api/character-cards", {
    method: "POST",
    body: JSON.stringify(card),
  });
}

export async function updateCharacterCard(name: string, card: Omit<CharacterCard, "_filename">): Promise<unknown> {
  return request(`/api/character-cards/${encodeURIComponent(name)}`, {
    method: "PUT",
    body: JSON.stringify(card),
  });
}

export async function deleteCharacterCard(name: string): Promise<unknown> {
  return request(`/api/character-cards/${encodeURIComponent(name)}`, { method: "DELETE" });
}

export async function activateCharacterCards(config: {
  aiCharacter?: string | null;
  userCharacter?: string | null;
}): Promise<unknown> {
  return request("/api/character-cards/activate", {
    method: "POST",
    body: JSON.stringify(config),
  });
}

export async function importCharacterCardsFromDir(path: string): Promise<{
  imported: { name: string; fileName: string; portrait: string }[];
  skipped: { file: string; reason: string }[];
}> {
  return request("/api/character-cards/import-dir", {
    method: "POST",
    body: JSON.stringify({ path }),
  });
}

export async function importCharacterCard(file: File): Promise<{ status: string; card: { filename: string; name: string; portrait?: string } }> {
  const formData = new FormData();
  formData.append("file", file);
  const res = await fetch("/api/character-cards/import", {
    method: "POST",
    body: formData,
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`${res.status}: ${body}`);
  }
  return res.json();
}

// ── Provider management ──

export interface ProviderInfo {
  alias: string;
  name: string;
  type: string;
  baseUrl: string | null;
  modelsUrl: string | null;
  defaultModel: string;
  contextLimit?: number;
  apiKeySet?: boolean;
  usedBy?: string[];
  options?: Record<string, unknown> | null;
}

export async function listProviders(): Promise<{ providers: ProviderInfo[] }> {
  return request("/api/providers");
}

export async function createProvider(provider: {
  alias: string;
  name: string;
  type: string;
  baseUrl?: string | null;
  modelsUrl?: string | null;
  apiKey?: string;
  defaultModel: string;
  contextLimit?: number;
  options?: Record<string, unknown>;
}): Promise<unknown> {
  return request("/api/providers", {
    method: "POST",
    body: JSON.stringify(provider),
  });
}

export async function updateProvider(
  alias: string,
  provider: {
    name?: string;
    type?: string;
    baseUrl?: string | null;
    apiKey?: string;
    defaultModel?: string;
  },
): Promise<unknown> {
  return request(`/api/providers/${encodeURIComponent(alias)}`, {
    method: "PUT",
    body: JSON.stringify(provider),
  });
}

export async function deleteProvider(alias: string): Promise<unknown> {
  return request(`/api/providers/${encodeURIComponent(alias)}`, {
    method: "DELETE",
  });
}

export async function testProvider(alias: string): Promise<{ alias: string; success: boolean; error?: string }> {
  return request("/api/providers/test", {
    method: "POST",
    body: JSON.stringify({ alias }),
  });
}

export async function fetchProviderModels(alias: string): Promise<{ models: string[] }> {
  return request(`/api/providers/${encodeURIComponent(alias)}/models`);
}

export async function fetchModelsForNew(provider: {
  type: string;
  url?: string | null;
  baseUrl?: string | null;
  modelsUrl?: string | null;
  apiKey?: string;
}): Promise<{ models: string[] }> {
  return request("/api/providers/fetch-models", {
    method: "POST",
    body: JSON.stringify(provider),
  });
}

// ── Agent model assignments ──

export interface AgentAssignments {
  orchestrator: string;
  proseWriter: string;
  librarian: string;
  forgeWriter: string;
  forgePlanner: string;
  forgeReviewer: string;
  research: string;
}

export async function getAgentModels(): Promise<{ assignments: AgentAssignments }> {
  return request("/api/agents/models");
}

export async function updateAgentModels(
  updates: Partial<AgentAssignments>,
): Promise<{ status: string; assignments: AgentAssignments }> {
  return request("/api/agents/models", {
    method: "PUT",
    body: JSON.stringify(updates),
  });
}

// ── Research project management ──

export async function listResearchProjects(): Promise<{ projects: string[] }> {
  return request("/api/research/projects");
}

export async function listResearchFiles(project: string): Promise<{ files: { name: string; path: string }[] }> {
  return request(`/api/research/projects/${encodeURIComponent(project)}`);
}

export async function readResearchFile(project: string, file: string): Promise<{ content: string }> {
  return request(`/api/research/projects/${encodeURIComponent(project)}/${encodeURIComponent(file)}`);
}

export async function deleteResearchFile(project: string, file: string): Promise<unknown> {
  return request(`/api/research/projects/${encodeURIComponent(project)}/${encodeURIComponent(file)}`, {
    method: "DELETE",
  });
}

export async function deleteResearchProject(project: string): Promise<unknown> {
  return request(`/api/research/projects/${encodeURIComponent(project)}`, {
    method: "DELETE",
  });
}
