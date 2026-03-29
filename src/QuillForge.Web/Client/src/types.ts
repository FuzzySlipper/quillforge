export type Mode = "general" | "writer" | "roleplay" | "forge" | "council";

export interface MessageVariant {
  content: string;
  responseType?: string;
  timestamp: number;
  portrait?: string | null;
  reasoning?: string | null;
}

export interface Message {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  responseType?: string;
  timestamp: number;
  /** Portrait URL for assistant messages (roleplay mode). */
  portrait?: string | null;
  /** Portrait URL for user messages (roleplay mode). */
  userPortrait?: string | null;
  /** Reasoning/thinking content from the model (e.g. DeepSeek reasoning). */
  reasoning?: string | null;
  /** Alternative responses (for swipe). Index 0 is the original. */
  variants?: MessageVariant[];
  /** Currently displayed variant index (0-based). */
  activeVariant?: number;
  /** Backend parent node ID — used for regeneration via parentId. */
  parentId?: string | null;
}

export interface Status {
  status: string;
  mode: Mode;
  project: string | null;
  file: string | null;
  loreFiles: number;
  loreSet: string;
  persona: string;
  writingStyle: string;
  model: string;
  conversationTurns: number;
  layout: string;
  contextLimit: number;
  loreTokens: number;
  personaTokens: number;
  historyTokens: number;
  diagnosticsLivePanel?: boolean;
}

export interface DiagnosticEntry {
  category: string;
  message: string;
  level: "info" | "warning" | "error";
}

export interface Profiles {
  personas: string[];
  loreSets: string[];
  writingStyles: string[];
  activePersona: string;
  activeLore: string;
  activeWritingStyle: string;
}

export interface ModeInfo {
  mode: Mode;
  project: string | null;
  file: string | null;
  character: string | null;
  pendingContent: string | null;
}

export interface ProjectEntry {
  name: string;
  files: string[];
}

export interface ProjectList {
  mode: string;
  directory: string;
  projects: ProjectEntry[];
}
