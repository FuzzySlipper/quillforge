import { useCallback, useEffect, useRef, useState } from "react";
import { acceptWriterPending, getMode, getStatus, getSessionUsage, loadSession, newSession, rejectWriterPending, sendChatStream, setMode as apiSetMode, conversationDeleteMessage, conversationFork } from "./api";
import type { Message, MessageVariant, Mode, ModeInfo, Status, DiagnosticEntry, SessionUsage, ReasoningArtifact } from "./types";
import { parseCommand, executeCommand } from "./commands";
import type { CommandContext } from "./commands";
import * as tts from "./tts";
import * as layoutManager from "./layout";
import type { LayoutConfig } from "./layout";
import * as artifactManager from "./artifacts";
import type { Artifact } from "./artifacts";
import AppShell from "./components/AppShell";
import AppRail from "./components/AppRail";
import AppInspector, { type InspectorSection } from "./components/AppInspector";
import AppStatusFooter from "./components/AppStatusFooter";
import ConversationPane from "./components/ConversationPane";
import GuideWorkspace from "./components/GuideWorkspace";
import InputBar from "./components/InputBar";
import ProfilePicker from "./components/ProfilePicker";
import ModeSwitcher from "./components/ModeSwitcher";
import ContextOverlay from "./components/ContextOverlay";
import LoreBrowser from "./components/LoreBrowser";
import PlotBrowser from "./components/PlotBrowser";
import PromptBrowser from "./components/PromptBrowser";
import LayoutPicker from "./components/LayoutPicker";
import ProviderManager from "./components/ProviderManager";
import SessionBrowser from "./components/SessionBrowser";
import CharacterCards from "./components/CharacterCards";
import TextThemePicker from "./components/TextThemePicker";
import CouncilConfigPanel from "./components/CouncilConfigPanel";
import ResearchPanel from "./components/ResearchPanel";
import RoleplayWorkspace from "./components/RoleplayWorkspace";
import ShellReasoningOverlay from "./components/ShellReasoningOverlay";
import WriterWorkspace from "./components/WriterWorkspace";
import * as textTheme from "./textTheme";
import type { TextTheme } from "./textTheme";
import { publishDesktopShellBridge } from "./desktopBridge";
import { MODE_DESCRIPTIONS, MODE_ICON_PATHS, MODE_LABELS } from "./modePresentation";

/** uuid() requires a secure context (HTTPS); fall back for plain HTTP. */
const uuid = (): string =>
  typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
      });

const RELEASES_URL = "https://github.com/FuzzySlipper/quillforge/releases";
const INSPECTOR_STORAGE_KEY_PREFIX = "qf-shell-inspector:";

function defaultInspectorOpen(mode: Mode): boolean {
  return mode === "guide" || mode === "roleplay" || mode === "research";
}

function readInspectorOpen(mode: Mode): boolean {
  const stored = window.localStorage.getItem(`${INSPECTOR_STORAGE_KEY_PREFIX}${mode}`);
  if (stored === "open") return true;
  if (stored === "closed") return false;
  return defaultInspectorOpen(mode);
}

function writeInspectorOpen(mode: Mode, open: boolean): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(`${INSPECTOR_STORAGE_KEY_PREFIX}${mode}`, open ? "open" : "closed");
}

function App() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [currentSessionId, setCurrentSessionId] = useState<string | null>(null);
  const [status, setStatus] = useState<Status | null>(null);
  const [mode, setMode] = useState<Mode>("guide");
  const [layout, setLayout] = useState<LayoutConfig>(layoutManager.getLayout());
  const [backgroundImage, setBackgroundImage] = useState<string | null>(layoutManager.getBackground());
  const [artifact, setArtifact] = useState<Artifact | null>(null);
  const [hasPending, setHasPending] = useState(false);
  const [modeInfo, setModeInfo] = useState<ModeInfo | null>(null);
  const [sending, setSending] = useState(false);
  const [streamStatus, setStreamStatus] = useState<string | null>(null);
  const [profileOpen, setProfileOpen] = useState(false);
  const [modeOpen, setModeOpen] = useState(false);
  const [reasoningOpen, setReasoningOpen] = useState(false);
  const [layoutOpen, setLayoutOpen] = useState(false);
  const [providerOpen, setProviderOpen] = useState(false);
  const [textThemeOpen, setTextThemeOpen] = useState(false);
  const [councilConfigOpen, setCouncilConfigOpen] = useState(false);
  const [inspectorOpen, setInspectorOpen] = useState(() => defaultInspectorOpen("guide"));
  const [inspectorSection, setInspectorSection] = useState<InspectorSection>("overview");
  const [portraits, setPortraits] = useState<{ filename: string; url: string }[]>([]);
  const [currentTextTheme, setCurrentTextTheme] = useState<TextTheme>(textTheme.getTheme());
  const abortRef = useRef<AbortController | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  // When true, the next response will be added as a variant to the last assistant message
  const addAsVariantRef = useRef(false);
  const [elapsed, setElapsed] = useState(0);
  const elapsedRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const [diagnosticEntries, setDiagnosticEntries] = useState<DiagnosticEntry[]>([]);
  const [sessionUsage, setSessionUsage] = useState<SessionUsage | null>(null);

  const applyModeInfo = useCallback((info: ModeInfo) => {
    setModeInfo(info);
    setHasPending(!!info.pendingContent);
  }, []);

  const refreshStatus = useCallback((sessionIdOverride?: string | null) => {
    const effectiveSessionId = sessionIdOverride ?? currentSessionId;
    getStatus(effectiveSessionId)
      .then((s) => {
        setStatus(s);
        setMode(s.mode);
      })
      .catch(() => setStatus(null));
    getMode(effectiveSessionId)
      .then(applyModeInfo)
      .catch(() => {
        setModeInfo(null);
        setHasPending(false);
      });
    if (effectiveSessionId) {
      getSessionUsage(effectiveSessionId)
        .then(setSessionUsage)
        .catch(() => setSessionUsage(null));
    } else {
      setSessionUsage(null);
    }
  }, [applyModeInfo, currentSessionId]);

  const handleSessionScopedRefresh = useCallback((sessionId?: string | null) => {
    if (sessionId) {
      setCurrentSessionId(sessionId);
    }

    refreshStatus(sessionId);
  }, [refreshStatus]);

  const handleNewSession = useCallback(async () => {
    const result = await newSession();
    setMessages([]);
    setCurrentSessionId(result.sessionId);
    setHasPending(false);
    setSessionUsage(null);
    setInspectorSection("overview");
    refreshStatus(result.sessionId);
  }, [refreshStatus]);

  const toggleInspector = useCallback(() => {
    setInspectorOpen((previous) => {
      const next = !previous;
      writeInspectorOpen(mode, next);
      return next;
    });
  }, [mode]);

  const openInspectorSection = useCallback((section: InspectorSection) => {
    setInspectorSection(section);
    setInspectorOpen(true);
    writeInspectorOpen(mode, true);
  }, [mode]);

  useEffect(() => {
    tts.init();
    textTheme.init();
    textTheme.setOnChange(setCurrentTextTheme);
    layoutManager.setOnLayoutChange(setLayout);
    layoutManager.setOnBackgroundChange(setBackgroundImage);
    artifactManager.setOnArtifactChange(setArtifact);
    artifactManager.init();

    // Fetch status first, then init layout with the configured default
    getStatus()
      .then((s) => {
        setStatus(s);
        setMode(s.mode);
        layoutManager.init(s.layout);
      })
      .catch(() => {
        setStatus(null);
        layoutManager.init();
      });
    getMode()
      .then(applyModeInfo)
      .catch(() => {
        setModeInfo(null);
        setHasPending(false);
      });
    fetch("/api/portraits")
      .then((r) => r.json())
      .then((d) => setPortraits(d.portraits ?? []))
      .catch(() => {});
  }, [applyModeInfo]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, streamStatus]);

  useEffect(() => {
    publishDesktopShellBridge(status, layout, currentTextTheme);
  }, [status, layout, currentTextTheme]);

  useEffect(() => {
    setInspectorOpen(readInspectorOpen(mode));
  }, [mode]);

  useEffect(() => {
    if (inspectorSection === "research" && mode !== "research") {
      setInspectorSection("overview");
    }
  }, [inspectorSection, mode]);

  function addResponseMessage(
    content: string,
    responseType: string,
    portrait?: string | null,
    reasoning?: string | null,
    reasoningArtifacts?: ReasoningArtifact[] | null,
    parentId?: string | null,
  ) {
    if (addAsVariantRef.current) {
      // Add as a variant to the last assistant message
      addAsVariantRef.current = false;
      setMessages((prev) => {
        const lastIdx = [...prev].reverse().findIndex((m) => m.role === "assistant");
        if (lastIdx === -1) {
          return [...prev, makeAssistantMsg(content, responseType, portrait, reasoning, reasoningArtifacts, parentId)];
        }
        const idx = prev.length - 1 - lastIdx;
        const msg = prev[idx];
        const variants: MessageVariant[] = msg.variants ?? [
          {
            content: msg.content,
            responseType: msg.responseType,
            timestamp: msg.timestamp,
            portrait: msg.portrait,
            reasoning: msg.reasoning,
            reasoningArtifacts: msg.reasoningArtifacts,
          },
        ];
        const newVariant: MessageVariant = { content, responseType, timestamp: Date.now(), portrait, reasoning, reasoningArtifacts: reasoningArtifacts ?? undefined };
        const newVariants = [...variants, newVariant];
        const newIdx = newVariants.length - 1;

        return [
          ...prev.slice(0, idx),
          {
            ...msg,
            content,
            responseType,
            portrait,
            reasoning,
            reasoningArtifacts: reasoningArtifacts ?? undefined,
            variants: newVariants,
            activeVariant: newIdx,
          },
          ...prev.slice(idx + 1),
        ];
      });
    } else {
      setMessages((prev) => [...prev, makeAssistantMsg(content, responseType, portrait, reasoning, reasoningArtifacts, parentId)]);
    }
    // Auto TTS for new assistant messages
    tts.onAssistantMessage(content);
  }

  function makeAssistantMsg(
    content: string,
    responseType: string,
    portrait?: string | null,
    reasoning?: string | null,
    reasoningArtifacts?: ReasoningArtifact[] | null,
    parentId?: string | null,
  ): Message {
    return {
      id: uuid(),
      role: "assistant",
      content,
      responseType,
      portrait,
      reasoning,
      reasoningArtifacts: reasoningArtifacts ?? undefined,
      parentId,
      timestamp: Date.now(),
    };
  }

  // Chat transport only: use this for conversational turns that should flow
  // through /api/chat/stream and the LLM/tool loop. Do not route deterministic
  // app mutations through chat text; use dedicated endpoints for delete/fork/
  // mode switches/accept-reject so the backend remains authoritative.
  async function doSend(text: string, regenerateParentId?: string | null) {
    setSending(true);
    setStreamStatus("Connecting...");
    setElapsed(0);
    setDiagnosticEntries([]);
    if (elapsedRef.current) clearInterval(elapsedRef.current);
    elapsedRef.current = setInterval(() => setElapsed((e) => e + 1), 1000);
    const abort = new AbortController();
    abortRef.current = abort;

    // Track the live-streaming message
    const streamMsgId = uuid();
    let streamingStarted = false;
    let accText = "";
    let accReasoning = "";

    try {
      await sendChatStream(
        text,
        (event) => {
          if (event.type === "status") {
            setStreamStatus(event.data.message as string);
          } else if (event.type === "tool") {
            setStreamStatus(`Using ${event.data.name}...`);
            // If we were streaming text before a tool call, clear the streaming msg
            if (streamingStarted) {
              setMessages((prev) => prev.filter((m) => m.id !== streamMsgId));
              streamingStarted = false;
              accText = "";
              accReasoning = "";
            }
          } else if (event.type === "text_delta") {
            accText += event.data.text as string;
            if (!streamingStarted) {
              streamingStarted = true;
              setStreamStatus(null);
              setMessages((prev) => [
                ...prev,
                {
                  id: streamMsgId,
                  role: "assistant",
                  content: accText,
                  responseType: "streaming",
                  reasoning: accReasoning || null,
                  timestamp: Date.now(),
                },
              ]);
            } else {
              const currentText = accText;
              const currentReasoning = accReasoning;
              setMessages((prev) =>
                prev.map((m) =>
                  m.id === streamMsgId
                    ? { ...m, content: currentText, reasoning: currentReasoning || null }
                    : m,
                ),
              );
            }
          } else if (event.type === "reasoning_delta") {
            accReasoning += event.data.text as string;
            if (streamingStarted) {
              const currentText = accText;
              const currentReasoning = accReasoning;
              setMessages((prev) =>
                prev.map((m) =>
                  m.id === streamMsgId
                    ? { ...m, content: currentText, reasoning: currentReasoning || null }
                    : m,
                ),
              );
            }
          } else if (event.type === "diagnostic") {
            setDiagnosticEntries((prev) => [...prev, {
              category: event.data.category as string,
              message: event.data.message as string,
              level: (event.data.level as "info" | "warning" | "error") || "info",
            }]);
          } else if (event.type === "done") {
            const responseSessionId = event.data.sessionId as string | null | undefined;
            if (responseSessionId) {
              setCurrentSessionId(responseSessionId);
            }

            // Update session usage from the cumulative tracker
            const usage = event.data.sessionUsage as SessionUsage | undefined;
            if (usage) {
              setSessionUsage(usage);
            }

            // Remove streaming message and add final one
            if (streamingStarted) {
              setMessages((prev) => prev.filter((m) => m.id !== streamMsgId));
            }
            const reasoning = (event.data.reasoning as string) || null;
            const reasoningArtifacts = (event.data.reasoningArtifacts as ReasoningArtifact[] | undefined) ?? undefined;
            const msg = makeAssistantMsg(
              event.data.content as string,
              event.data.responseType as string,
              event.data.portrait as string | null | undefined,
              reasoning,
              reasoningArtifacts,
            );
            // Apply user portrait to the preceding user message
            const userPortrait = event.data.userPortrait as string | null | undefined;
            if (userPortrait) {
              setMessages((prev) =>
                prev.map((m, i) => {
                  // Find the last user message
                  const isLastUser = m.role === "user" && !prev.slice(i + 1).some((n) => n.role === "user");
                  return isLastUser ? { ...m, userPortrait } : m;
                }),
              );
            }
            addResponseMessage(
              msg.content,
              msg.responseType || "discussion",
              msg.portrait,
              msg.reasoning,
              msg.reasoningArtifacts,
              event.data.parentId as string | null | undefined,
            );
            setStreamStatus(null);
            refreshStatus(responseSessionId);
          } else if (event.type === "persisted") {
            // Update message IDs from client UUIDs to backend GUIDs
            const nodeId = event.data.nodeId as string | null;
            const userNodeId = event.data.userNodeId as string | null;
            if (nodeId) {
              setMessages((prev) => {
                const updated = [...prev];
                for (let i = updated.length - 1; i >= 0; i--) {
                  if (updated[i].role === "assistant") {
                    updated[i] = { ...updated[i], id: nodeId };
                    break;
                  }
                }
                return updated;
              });
            }
            if (userNodeId) {
              setMessages((prev) => {
                const updated = [...prev];
                let assistantIdx = -1;
                for (let i = updated.length - 1; i >= 0; i--) {
                  if (updated[i].role === "assistant") { assistantIdx = i; break; }
                }
                if (assistantIdx > 0) {
                  for (let i = assistantIdx - 1; i >= 0; i--) {
                    if (updated[i].role === "user") {
                      updated[i] = { ...updated[i], id: userNodeId };
                      break;
                    }
                  }
                }
                return updated;
              });
            }
          } else if (event.type === "error") {
            if (streamingStarted) {
              setMessages((prev) => prev.filter((m) => m.id !== streamMsgId));
            }
            addResponseMessage(`Error: ${event.data.message}`, "error");
            setStreamStatus(null);
          }
        },
        abort.signal,
        currentSessionId,
        regenerateParentId,
      );
    } catch (err) {
      if (streamingStarted) {
        setMessages((prev) => prev.filter((m) => m.id !== streamMsgId));
      }
      if ((err as Error).name !== "AbortError") {
        addResponseMessage(
          `Error: ${err instanceof Error ? err.message : "Connection failed"}`,
          "error",
        );
      }
      setStreamStatus(null);
    } finally {
      setSending(false);
      abortRef.current = null;
      if (elapsedRef.current) { clearInterval(elapsedRef.current); elapsedRef.current = null; }
    }
  }

  async function handleSend(text: string) {
    const parsed = parseCommand(text);

    if (parsed) {
      // Show the command in chat as a user message
      const cmdMsg: Message = {
        id: uuid(),
        role: "user",
        content: text,
        responseType: "command",
        timestamp: Date.now(),
      };
      setMessages((prev) => [...prev, cmdMsg]);

      // Build command context
      const ctx: CommandContext = {
        status,
        sessionId: currentSessionId,
        mode,
        messages,
        openProfile: () => setProfileOpen(true),
        openMode: () => setModeOpen(true),
        openLore: () => openInspectorSection("lore"),
        openContext: () => openInspectorSection("context"),
        newSession: async () => {
          const result = await newSession();
          setMessages([]);
          setCurrentSessionId(result.sessionId);
          setHasPending(false);
          setSessionUsage(null);
          setInspectorSection("overview");
          refreshStatus(result.sessionId);
        },
        clearMessages: () => setMessages([]),
        addChatMessage: (partial) => {
          const msg: Message = { ...partial, id: uuid(), timestamp: Date.now() };
          setMessages((prev) => [...prev, msg]);
        },
        setMode: async (m: Mode, project?: string, file?: string, character?: string) => {
          const result = await apiSetMode(m, project, file, character, currentSessionId);
          if (result.sessionId) {
            setCurrentSessionId(result.sessionId);
          }
          if (result.notice) {
            addSystemMessage(result.notice);
          }
          refreshStatus(result.sessionId ?? currentSessionId);
        },
        refreshStatus,
        streamRequest: async (fetcher) => {
          // Reuse the same streaming UI as doSend, with text_delta support
          setSending(true);
          setStreamStatus("Connecting...");
          setElapsed(0);
          if (elapsedRef.current) clearInterval(elapsedRef.current);
          elapsedRef.current = setInterval(() => setElapsed((e) => e + 1), 1000);
          const abort = new AbortController();
          abortRef.current = abort;

          const reqStreamMsgId = uuid();
          let reqStreamStarted = false;
          let reqAccText = "";

          try {
            await fetcher(
              (event) => {
                if (event.type === "status") {
                  setStreamStatus(event.data.message as string);
                } else if (event.type === "tool") {
                  setStreamStatus(`Using ${event.data.name}...`);
                } else if (event.type === "text_delta") {
                  reqAccText += event.data.text as string;
                  if (!reqStreamStarted) {
                    reqStreamStarted = true;
                    setStreamStatus(null);
                    setMessages((prev) => [
                      ...prev,
                      { id: reqStreamMsgId, role: "assistant", content: reqAccText, responseType: "streaming", timestamp: Date.now() },
                    ]);
                  } else {
                    const text = reqAccText;
                    setMessages((prev) =>
                      prev.map((m) => m.id === reqStreamMsgId ? { ...m, content: text } : m),
                    );
                  }
                } else if (event.type === "done") {
                  if (reqStreamStarted) {
                    setMessages((prev) => prev.filter((m) => m.id !== reqStreamMsgId));
                  }
                  addResponseMessage(
                    event.data.content as string,
                    event.data.responseType as string,
                    event.data.portrait as string | null | undefined,
                    (event.data.reasoning as string | null | undefined) ?? null,
                    (event.data.reasoningArtifacts as ReasoningArtifact[] | undefined) ?? undefined,
                  );
                  setStreamStatus(null);
                  refreshStatus();
                } else if (event.type === "error") {
                  if (reqStreamStarted) {
                    setMessages((prev) => prev.filter((m) => m.id !== reqStreamMsgId));
                  }
                  addResponseMessage(`Error: ${event.data.message}`, "error");
                  setStreamStatus(null);
                }
              },
              abort.signal,
            );
          } catch (err) {
            if ((err as Error).name !== "AbortError") {
              addResponseMessage(
                `Error: ${err instanceof Error ? err.message : "Connection failed"}`,
                "error",
              );
            }
            setStreamStatus(null);
          } finally {
            setSending(false);
            abortRef.current = null;
            if (elapsedRef.current) { clearInterval(elapsedRef.current); elapsedRef.current = null; }
          }
        },
      };

      const result = await executeCommand(parsed.name, parsed.args, ctx);
      if (result === null) {
        // Unknown command — show error
        addSystemMessage(`Unknown command \`/${parsed.name}\`. Type \`/help\` for available commands.`);
      } else if (result.output) {
        addSystemMessage(result.output);
      }

      // Auto-send a follow-up chat message if the command requested it
      if (result?.autoSend) {
        const autoMsg: Message = {
          id: uuid(),
          role: "user",
          content: result.autoSend,
          timestamp: Date.now(),
        };
        setMessages((prev) => [...prev, autoMsg]);
        await doSend(result.autoSend);
      }
      return;
    }

    // Normal LLM message
    const userMsg: Message = {
      id: uuid(),
      role: "user",
      content: text,
      timestamp: Date.now(),
    };
    setMessages((prev) => [...prev, userMsg]);
    addAsVariantRef.current = false;
    await doSend(text);
  }

  function addSystemMessage(content: string) {
    const msg: Message = {
      id: uuid(),
      role: "system",
      content,
      timestamp: Date.now(),
    };
    setMessages((prev) => [...prev, msg]);
  }

  function handleStop() {
    abortRef.current?.abort();
  }

  function mapLoadedMessages(
    msgs: Array<{
      id: string;
      role: string;
      content: string;
      createdAt: string;
      reasoning?: string | null;
      reasoningArtifacts?: ReasoningArtifact[] | null;
      parentId?: string | null;
      variants?: Array<{ content: string; createdAt: string; reasoning?: string | null; reasoningArtifacts?: ReasoningArtifact[] | null }> | null;
    }>,
  ): Message[] {
    return msgs.map((m) => ({
      id: m.id,
      role: m.role as "user" | "assistant",
      content: m.content,
      timestamp: new Date(m.createdAt).getTime() || Date.now(),
      reasoning: m.reasoning ?? null,
      reasoningArtifacts: m.reasoningArtifacts ?? undefined,
      parentId: m.parentId ?? undefined,
      variants: m.variants?.map((v) => ({
        content: v.content,
        responseType: undefined,
        timestamp: new Date(v.createdAt).getTime(),
        reasoning: v.reasoning ?? null,
        reasoningArtifacts: v.reasoningArtifacts ?? undefined,
      })),
      activeVariant: m.variants ? 0 : undefined,
    }));
  }

  async function reloadSessionMessages(sessionId: string) {
    const loaded = await loadSession(sessionId);
    setMessages(mapLoadedMessages(loaded.messages));
    setCurrentSessionId(loaded.sessionId);
    setHasPending(false);
    refreshStatus(loaded.sessionId);
  }

  function handleEditMessage(id: string, newContent: string) {
    setMessages((prev) =>
      prev.map((m) => (m.id === id ? { ...m, content: newContent } : m)),
    );
  }

  async function handleRetry(id: string) {
    // Find the message being retried
    const idx = messages.findIndex((m) => m.id === id);
    if (idx === -1) return;

    const msg = messages[idx];

    if (msg.role === "user") {
      // Retrying a user message: trim everything after it and re-send
      setMessages((prev) => prev.slice(0, idx + 1));
      addAsVariantRef.current = false;
      await doSend(msg.content);
    } else if (msg.role === "assistant" && msg.parentId) {
      // Retry as a swipeable variant using the backend parentId
      addAsVariantRef.current = true;
      await doSend("", msg.parentId);
    } else if (msg.role === "assistant") {
      // Fallback for messages without parentId (legacy): send "regenerate" text
      addAsVariantRef.current = true;
      await doSend("regenerate");
    }
  }

  function handleSwipe(id: string, direction: "prev" | "next") {
    setMessages((prev) =>
      prev.map((m) => {
        if (m.id !== id || !m.variants) return m;
        const current = m.activeVariant ?? 0;
        const next = direction === "prev" ? current - 1 : current + 1;
        if (next < 0 || next >= m.variants.length) return m;
        const variant = m.variants[next];
        return {
          ...m,
          content: variant.content,
          responseType: variant.responseType,
          portrait: variant.portrait,
          reasoning: variant.reasoning,
          reasoningArtifacts: variant.reasoningArtifacts,
          activeVariant: next,
        };
      }),
    );
  }

  async function handleAccept() {
    if (!currentSessionId) {
      addSystemMessage("No active session for Writer accept.");
      return;
    }

    try {
      const result = await acceptWriterPending(currentSessionId);
      addSystemMessage(`Accepted pending draft and saved to \`${result.savedPath}\`.`);
      refreshStatus(result.sessionId);
    } catch (err) {
      addSystemMessage(`Failed to accept pending draft: ${err instanceof Error ? err.message : "unknown error"}`);
    }
  }

  async function handleReject() {
    if (!currentSessionId) {
      addSystemMessage("No active session for Writer reject.");
      return;
    }

    try {
      const result = await rejectWriterPending(currentSessionId);
      addSystemMessage("Rejected pending draft.");
      refreshStatus(result.sessionId);
    } catch (err) {
      addSystemMessage(`Failed to reject pending draft: ${err instanceof Error ? err.message : "unknown error"}`);
    }
  }

  async function handleRegenerate() {
    // Find the last assistant message's parentId for regeneration
    const lastAssistant = [...messages].reverse().find((m) => m.role === "assistant");
    addAsVariantRef.current = true;
    if (lastAssistant?.parentId) {
      await doSend("", lastAssistant.parentId);
    } else {
      // Fallback for messages without parentId
      await doSend("regenerate");
    }
  }

  async function handleDeleteLast() {
    if (!currentSessionId) return;

    const lastAssistant = [...messages].reverse().find((m) => m.role === "assistant");
    if (!lastAssistant) return;

    try {
      // This is a real conversation mutation, not a chat prompt. Keep it on
      // the explicit delete endpoint instead of sending magic text via doSend.
      await conversationDeleteMessage(currentSessionId, lastAssistant.id);
      await reloadSessionMessages(currentSessionId);
    } catch (err) {
      addSystemMessage(`Failed to delete last roleplay turn: ${err instanceof Error ? err.message : "unknown error"}`);
    }
  }

  async function handleDeleteMessage(id: string) {
    if (!currentSessionId) return;

    try {
      await conversationDeleteMessage(currentSessionId, id);
      // Remove from frontend: delete the message and its pair
      const msg = messages.find((m) => m.id === id);
      if (!msg) return;
      setMessages((prev) => {
        const msgIdx = prev.findIndex((m) => m.id === id);
        if (msgIdx === -1) return prev;
        if (msg.role === "user") {
          const next = prev[msgIdx + 1];
          const end = next && next.role === "assistant" ? msgIdx + 2 : msgIdx + 1;
          return [...prev.slice(0, msgIdx), ...prev.slice(end)];
        } else {
          const prev2 = prev[msgIdx - 1];
          const start = prev2 && prev2.role === "user" ? msgIdx - 1 : msgIdx;
          return [...prev.slice(0, start), ...prev.slice(msgIdx + 1)];
        }
      });
      refreshStatus();
    } catch (err) {
      addSystemMessage(`Failed to delete message: ${err instanceof Error ? err.message : "unknown error"}`);
    }
  }

  async function handleForkMessage(id: string) {
    if (!currentSessionId) return;

    try {
      const result = await conversationFork(currentSessionId, id);
      addSystemMessage(`Forked conversation saved as session (${result.messageCount} turns). Open Sessions to load it.`);
    } catch {
      addSystemMessage("Failed to fork conversation.");
    }
  }

  const hasAssistantMessages = messages.some((m) => m.role === "assistant");
  const updateVersion = status?.update?.version ?? "a newer version";
  const updateUrl = status?.update?.url ?? RELEASES_URL;
  const workspaceTitle = status?.project ?? MODE_LABELS[mode];
  const workspaceSubtitle = status?.file ?? MODE_DESCRIPTIONS[mode];
  const modelSummary = status?.model ? status.model.split("-").slice(0, 2).join("-") : "loading";
  const updateBanner = status?.update?.available ? (
    <div className="flex items-center justify-between gap-3 border-b border-accent/20 bg-accent/10 px-4 py-2 text-sm">
      <div className="min-w-0">
        <span className="font-medium text-accent">Update available.</span>{" "}
        <span className="text-text-muted">
          Download {updateVersion} from GitHub and replace the app binary. Your <code>user/</code> folder should stay as-is.
        </span>
      </div>
      <a
        href={updateUrl}
        target="_blank"
        rel="noreferrer"
        className="shrink-0 rounded-md bg-surface-alt px-3 py-1.5 text-xs text-text-muted transition-colors hover:text-text"
      >
        Download
      </a>
    </div>
  ) : null;

  function buildEmptyState(activeMode: Mode, title: string, description: string) {
    return (
      <div className="flex flex-1 items-center justify-center px-6 py-8">
        <div className="max-w-xl text-center text-text-muted">
          {status?.status === "ready" && (
            <div className="mb-3 flex justify-center">
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-surface-alt/80 ring-1 ring-border/70 shadow-lg">
                <img
                  src={MODE_ICON_PATHS[activeMode]}
                  alt=""
                  aria-hidden="true"
                  className="h-9 w-9"
                />
              </div>
            </div>
          )}
          <p className="mb-2 text-lg text-text">{title}</p>
          <p className="text-sm leading-6">
            {status?.status === "ready" ? description : "Connecting..."}
          </p>
        </div>
      </div>
    );
  }

  const conversationPaneProps = {
    messages,
    diagnosticEntries,
    diagnosticsEnabled: !!status?.diagnosticsLivePanel,
    sending,
    streamStatus,
    elapsed,
    messagesEndRef,
    onStop: handleStop,
    onEdit: handleEditMessage,
    onRetry: handleRetry,
    onSwipe: handleSwipe,
    onDelete: handleDeleteMessage,
    onFork: handleForkMessage,
  };

  const fallbackConversationPane = (
    <ConversationPane
      {...conversationPaneProps}
      mode={mode}
      emptyState={buildEmptyState(
        mode,
        "Ready to go",
        `${MODE_LABELS[mode]} mode · ${status?.loreFiles ?? 0} lore files loaded`,
      )}
    />
  );

  const workspaceContent = (() => {
    switch (mode) {
      case "guide":
        return (
          <GuideWorkspace
            status={status}
            updateBanner={updateBanner}
            conversationPane={(
              <ConversationPane
                {...conversationPaneProps}
                mode="guide"
                emptyState={buildEmptyState(
                  "guide",
                  "Guide is ready",
                  "Ask for setup help, workflow advice, or orientation before you dive into drafting and scene work.",
                )}
              />
            )}
            inputBar={(
              <InputBar
                onSend={handleSend}
                disabled={sending}
                placeholder="Ask Guide what to do next..."
              />
            )}
            sending={sending}
            onOpenMode={() => setModeOpen(true)}
            onNewSession={() => {
              void handleNewSession();
            }}
            onOpenSessions={() => openInspectorSection("sessions")}
            onOpenSection={openInspectorSection}
            onQuickPrompt={(prompt) => {
              void handleSend(prompt);
            }}
          />
        );
      case "writer":
        return (
          <WriterWorkspace
            status={status}
            modeInfo={modeInfo}
            messages={messages}
            hasPending={hasPending}
            sending={sending}
            updateBanner={updateBanner}
            conversationPane={(
              <ConversationPane
                {...conversationPaneProps}
                mode="writer"
                emptyState={buildEmptyState(
                  "writer",
                  "Quill support is ready",
                  "Ask for a new passage, revision pass, or target-aware rewrite while your manuscript stays visible beside the chat.",
                )}
              />
            )}
            inputBar={(
              <InputBar
                onSend={handleSend}
                disabled={sending}
                placeholder="Ask Quill for a draft, revision, or save-target change..."
              />
            )}
            onOpenSection={openInspectorSection}
            onAccept={handleAccept}
            onReject={handleReject}
            onRegenerate={handleRegenerate}
          />
        );
      case "roleplay":
        return (
          <RoleplayWorkspace
            status={status}
            hasMessages={hasAssistantMessages}
            sending={sending}
            updateBanner={updateBanner}
            conversationPane={(
              <ConversationPane
                {...conversationPaneProps}
                mode="roleplay"
                emptyState={buildEmptyState(
                  "roleplay",
                  "Scene stage is ready",
                  "Open a saved session or start the next turn to bring the cast, portraits, and scene transcript to life.",
                )}
              />
            )}
            inputBar={(
              <InputBar
                onSend={handleSend}
                disabled={sending}
                placeholder="Continue the scene..."
              />
            )}
            onOpenSection={openInspectorSection}
            onRegenerate={handleRegenerate}
            onDeleteLast={handleDeleteLast}
          />
        );
      default:
        return (
          <div className="flex h-full min-h-0 flex-col">
            <div className="border-b border-border/70 px-6 py-5">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="min-w-0">
                  <div className="qf-shell-folio">{MODE_LABELS[mode]} workspace</div>
                  <h1 className="qf-shell-title mt-1">{workspaceTitle}</h1>
                  <p className="qf-shell-subtitle mt-2 max-w-3xl">{workspaceSubtitle}</p>
                </div>

                <div className="flex flex-wrap items-center gap-2 text-[12px] text-text-muted">
                  <span className="qf-shell-card px-3 py-1.5">
                    profile · <span className="text-text">{status?.profile ?? "loading"}</span>
                  </span>
                  <span className="qf-shell-card px-3 py-1.5">
                    lore · <span className="text-text">{status?.loreFiles ?? 0}</span>
                  </span>
                  <span className="qf-shell-card px-3 py-1.5">
                    model · <span className="text-text">{modelSummary}</span>
                  </span>
                </div>
              </div>
            </div>

            {updateBanner}
            {fallbackConversationPane}
            <InputBar onSend={handleSend} disabled={sending} />
          </div>
        );
    }
  })();

  const inspectorContent = (() => {
    const handleInlineClose = () => setInspectorSection("overview");

    switch (inspectorSection) {
      case "sessions":
        return (
          <SessionBrowser
            open
            variant="inline"
            onClose={handleInlineClose}
            onLoad={(sessionId, msgs) => {
              setMessages(mapLoadedMessages(msgs));
              setCurrentSessionId(sessionId);
              setHasPending(false);
              refreshStatus(sessionId);
            }}
          />
        );
      case "lore":
        return (
          <LoreBrowser
            open
            variant="inline"
            onClose={handleInlineClose}
            onChanged={handleSessionScopedRefresh}
            sessionId={currentSessionId}
          />
        );
      case "plots":
        return (
          <PlotBrowser
            open
            variant="inline"
            onClose={handleInlineClose}
            onChanged={refreshStatus}
            sessionId={currentSessionId}
          />
        );
      case "prompts":
        return (
          <PromptBrowser
            open
            variant="inline"
            onClose={handleInlineClose}
            onChanged={refreshStatus}
          />
        );
      case "characters":
        return (
          <CharacterCards
            open
            variant="inline"
            onClose={handleInlineClose}
            onChanged={handleSessionScopedRefresh}
            sessionId={currentSessionId}
            portraits={portraits}
          />
        );
      case "context":
        return (
          <ContextOverlay
            open
            variant="inline"
            onClose={handleInlineClose}
            status={status}
            sessionId={currentSessionId}
          />
        );
      case "research":
        return mode === "research" ? (
          <ResearchPanel
            open
            variant="inline"
            onClose={handleInlineClose}
          />
        ) : null;
      case "overview":
      default:
        return null;
    }
  })();

  return (
    <>
      <AppShell
        backgroundImage={backgroundImage}
        inspectorOpen={inspectorOpen}
        onToggleInspector={toggleInspector}
        rail={(
          <AppRail
            status={status}
            mode={mode}
            inspectorOpen={inspectorOpen}
            onOpenMode={() => setModeOpen(true)}
            onNewSession={handleNewSession}
            onOpenSessions={() => openInspectorSection("sessions")}
            onToggleInspector={toggleInspector}
            onOpenProfile={() => setProfileOpen(true)}
            onOpenProviders={() => setProviderOpen(true)}
            onOpenTextTheme={() => setTextThemeOpen(true)}
          />
        )}
        inspector={(
          <AppInspector
            status={status}
            mode={mode}
            modeInfo={modeInfo}
            layoutName={layout.name}
            textThemeName={currentTextTheme.name}
            artifact={artifact}
            section={inspectorSection}
            onSelectSection={openInspectorSection}
            onOpenLayout={() => setLayoutOpen(true)}
            onOpenCouncilConfig={() => setCouncilConfigOpen(true)}
          >
            {inspectorContent}
          </AppInspector>
        )}
        footer={(
          <AppStatusFooter
            status={status}
            usage={sessionUsage}
            messages={messages}
            inspectorOpen={inspectorOpen}
            onToggleInspector={toggleInspector}
            onOpenContext={() => openInspectorSection("context")}
            onOpenReasoning={() => setReasoningOpen(true)}
          />
        )}
      >
        {workspaceContent}
      </AppShell>

      <ProfilePicker
        open={profileOpen}
        onClose={() => setProfileOpen(false)}
        onSwitched={handleSessionScopedRefresh}
        sessionId={currentSessionId}
      />
      <ModeSwitcher
        open={modeOpen}
        onClose={() => setModeOpen(false)}
        onSwitched={(sessionId, notice) => {
          if (notice) {
            addSystemMessage(notice);
          }
          handleSessionScopedRefresh(sessionId);
        }}
        sessionId={currentSessionId}
      />
      <ShellReasoningOverlay
        open={reasoningOpen}
        onClose={() => setReasoningOpen(false)}
        messages={messages}
      />
      <LayoutPicker
        open={layoutOpen}
        onClose={() => setLayoutOpen(false)}
      />
      <ProviderManager
        open={providerOpen}
        onClose={() => setProviderOpen(false)}
        onChanged={refreshStatus}
      />
      <TextThemePicker
        open={textThemeOpen}
        onClose={() => setTextThemeOpen(false)}
        onChanged={() => setCurrentTextTheme(textTheme.getTheme())}
      />
      <CouncilConfigPanel
        open={councilConfigOpen}
        onClose={() => setCouncilConfigOpen(false)}
      />
    </>
  );
}

export default App;
