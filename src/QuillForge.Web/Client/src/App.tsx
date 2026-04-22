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
import LayoutShell from "./components/LayoutShell";
import HeaderBar from "./components/HeaderBar";
import MessageBubble from "./components/MessageBubble";
import InputBar from "./components/InputBar";
import ProfilePicker from "./components/ProfilePicker";
import ModeSwitcher from "./components/ModeSwitcher";
import ContextOverlay from "./components/ContextOverlay";
import WriterControls from "./components/WriterControls";
import RoleplayControls from "./components/RoleplayControls";
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
import DiagnosticsPanel from "./components/DiagnosticsPanel";
import TokenUsageBar from "./components/TokenUsageBar";
import * as textTheme from "./textTheme";
import type { TextTheme } from "./textTheme";
import { publishDesktopShellBridge } from "./desktopBridge";
import { MODE_ICON_PATHS, MODE_LABELS } from "./modePresentation";

/** uuid() requires a secure context (HTTPS); fall back for plain HTTP. */
const uuid = (): string =>
  typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
      });

const RELEASES_URL = "https://github.com/FuzzySlipper/quillforge/releases";

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
  const [contextOpen, setContextOpen] = useState(false);
  const [loreOpen, setLoreOpen] = useState(false);
  const [plotOpen, setPlotOpen] = useState(false);
  const [promptsOpen, setPromptsOpen] = useState(false);
  const [layoutOpen, setLayoutOpen] = useState(false);
  const [providerOpen, setProviderOpen] = useState(false);
  const [sessionsOpen, setSessionsOpen] = useState(false);
  const [charactersOpen, setCharactersOpen] = useState(false);
  const [textThemeOpen, setTextThemeOpen] = useState(false);
  const [councilConfigOpen, setCouncilConfigOpen] = useState(false);
  const [researchOpen, setResearchOpen] = useState(false);
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
  }, [currentSessionId]);

  const handleSessionScopedRefresh = useCallback((sessionId?: string | null) => {
    if (sessionId) {
      setCurrentSessionId(sessionId);
    }

    refreshStatus(sessionId);
  }, [refreshStatus]);

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
        openLore: () => setLoreOpen(true),
        openContext: () => setContextOpen(true),
        newSession: async () => {
          const result = await newSession();
          setMessages([]);
          setCurrentSessionId(result.sessionId);
          setHasPending(false);
          setSessionUsage(null);
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

  const chatContent = (
    <div className="h-dvh flex flex-col bg-bg">
      <HeaderBar
        status={status}
        layoutName={layout.name}
        mode={mode}
        onOpenProfile={() => setProfileOpen(true)}
        onOpenMode={() => setModeOpen(true)}
        onOpenContext={() => setContextOpen(true)}
        onOpenLore={() => setLoreOpen(true)}
        onOpenPlots={() => setPlotOpen(true)}
        onOpenPrompts={() => setPromptsOpen(true)}
        onOpenLayout={() => setLayoutOpen(true)}
        onOpenProviders={() => setProviderOpen(true)}
        onOpenCouncilConfig={() => setCouncilConfigOpen(true)}
        onOpenResearch={() => setResearchOpen(true)}
        onOpenSessions={() => setSessionsOpen(true)}
        onOpenCharacters={() => setCharactersOpen(true)}
        onOpenTextTheme={() => setTextThemeOpen(true)}
        textThemeName={currentTextTheme.name}
        onNewSession={async () => {
          const result = await newSession();
          setMessages([]);
          setCurrentSessionId(result.sessionId);
          setHasPending(false);
          setSessionUsage(null);
          refreshStatus(result.sessionId);
        }}
      />

      {status?.update?.available && (
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
      )}

      <div className="flex-1 overflow-y-auto p-4 flex flex-col gap-3">
        {messages.length === 0 && (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center text-text-muted">
              {status?.status === "ready" && (
                <div className="mb-3 flex justify-center">
                  <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-surface-alt/80 ring-1 ring-border/70 shadow-lg">
                    <img src={MODE_ICON_PATHS[status.mode]} alt="" aria-hidden="true" className="h-9 w-9" />
                  </div>
                </div>
              )}
              <p className="text-lg mb-2">Ready to go</p>
              <p className="text-sm">
                {status?.status === "ready"
                  ? `${MODE_LABELS[status.mode]} mode · ${status.loreFiles} lore files loaded`
                  : "Connecting..."}
              </p>
            </div>
          </div>
        )}
        {messages.map((msg, i) => {
          // Compute index excluding system messages (matches backend history indices)
          const msgIndex = msg.role === "system" ? 0 : messages.slice(0, i + 1).filter((m) => m.role !== "system").length;
          return (
          <MessageBubble
            key={msg.id}
            message={msg}
            index={msgIndex}
            mode={mode}
            onEdit={msg.role !== "system" ? handleEditMessage : undefined}
            onRetry={msg.role !== "system" ? handleRetry : undefined}
            onSwipe={msg.role === "assistant" ? handleSwipe : undefined}
            onDelete={msg.role !== "system" ? handleDeleteMessage : undefined}
            onFork={msg.role !== "system" ? handleForkMessage : undefined}
          />
          );
        })}
        <DiagnosticsPanel entries={diagnosticEntries} enabled={!!status?.diagnosticsLivePanel} />
        {sending && (
          <div className="flex items-center gap-2 text-text-muted italic text-sm px-4 py-2">
            <span className="inline-block w-2 h-2 rounded-full bg-accent animate-pulse" />
            <span>{streamStatus || "Working..."}</span>
            <span className="text-text-muted/40 text-xs font-mono tabular-nums">{elapsed}s</span>
            <button
              onClick={handleStop}
              className="ml-auto text-xs bg-surface-alt hover:bg-border text-text-muted hover:text-text rounded px-2 py-1 transition-colors"
            >
              Stop
            </button>
          </div>
        )}
        <div ref={messagesEndRef} />
      </div>

      {mode === "writer" && (
        <WriterControls
          hasPending={hasPending}
          currentProject={status?.project ?? null}
          currentFile={status?.file ?? null}
          pendingProject={modeInfo?.pendingProject ?? null}
          pendingFile={modeInfo?.pendingFile ?? null}
          onAccept={handleAccept}
          onReject={handleReject}
          onRegenerate={handleRegenerate}
          disabled={sending}
        />
      )}

      {mode === "roleplay" && (
        <RoleplayControls
          hasMessages={hasAssistantMessages}
          onRegenerate={handleRegenerate}
          onDeleteLast={handleDeleteLast}
          disabled={sending}
        />
      )}

      <InputBar onSend={handleSend} disabled={sending} />
      <TokenUsageBar usage={sessionUsage} />

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
      <ContextOverlay
        open={contextOpen}
        onClose={() => setContextOpen(false)}
        status={status}
        sessionId={currentSessionId}
      />
      <LoreBrowser
        open={loreOpen}
        onClose={() => setLoreOpen(false)}
        onChanged={handleSessionScopedRefresh}
        sessionId={currentSessionId}
      />
      <PlotBrowser
        open={plotOpen}
        onClose={() => setPlotOpen(false)}
        onChanged={refreshStatus}
        sessionId={currentSessionId}
      />
      <PromptBrowser
        open={promptsOpen}
        onClose={() => setPromptsOpen(false)}
        onChanged={refreshStatus}
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
      <CharacterCards
        open={charactersOpen}
        onClose={() => setCharactersOpen(false)}
        onChanged={handleSessionScopedRefresh}
        sessionId={currentSessionId}
        portraits={portraits}
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
      <ResearchPanel
        open={researchOpen}
        onClose={() => setResearchOpen(false)}
      />
      <SessionBrowser
        open={sessionsOpen}
        onClose={() => setSessionsOpen(false)}
        onLoad={(sessionId, msgs) => {
          setMessages(mapLoadedMessages(msgs));
          setCurrentSessionId(sessionId);
          setHasPending(false);
          refreshStatus(sessionId);
        }}
      />
    </div>
  );

  return (
    <LayoutShell layout={layout} chatContent={chatContent} artifact={artifact} backgroundImage={backgroundImage} />
  );
}

export default App;
