import type { CSSProperties } from "react";
import { useEffect, useState } from "react";
import type { Mode } from "./types";
import { MODE_ICON_PATHS } from "./modePresentation";
import "./story-tour.css";

type TourFlowStep = {
  label: string;
  detail: string;
  items?: string[];
  kind?: "cluster" | "terminal";
};

type TourModeSpec = {
  id: Mode;
  label: string;
  badge: string;
  family: string;
  accent: string;
  promise: string;
  feelsLike: string;
  whenToUse: string;
  returns: string;
  userPrompt: string;
  visibleTitle: string;
  visibleResult: string;
  backstageCopy: string;
  backstageNotes: string[];
  guardrails: string[];
  flow: TourFlowStep[];
  sampleOutput: string;
};

type ScenePipelineMode = "writer" | "roleplay";

type ScenePipelineSpec = {
  label: string;
  badge: string;
  accent: string;
  intro: string;
  userLabel: string;
  userLine: string;
  returnLabel: string;
  returnLine: string;
  outcome: string;
  appNote: string;
};

type ProviderPrinciple = {
  title: string;
  body: string;
  emphasis: string;
};

type ProviderRoleGuide = {
  agent: string;
  recommendation: string;
  why: string;
};

const MODE_ORDER: Mode[] = ["guide", "writer", "roleplay", "lore", "council", "research", "forge", "games"];

const TOUR_MODES: Record<Mode, TourModeSpec> = {
  guide: {
    id: "guide",
    label: "Guide",
    badge: "Start Here",
    family: "Front Desk",
    accent: "#8ae7c1",
    promise: "Explains the rooms, checks the runway, and points you toward the right workflow.",
    feelsLike: "A sharp stage manager for the whole studio.",
    whenToUse: "When you are unsure which mode fits, or you need onboarding and troubleshooting before story work starts.",
    returns: "A clearer next step rather than hidden generation.",
    userPrompt: "I have a drowned-city story idea, but I do not know whether I need Writer, Research, or Forge.",
    visibleTitle: "What shows up in chat",
    visibleResult:
      "Guide maps the modes, flags obvious setup gaps, and steers the user toward the right room instead of quietly doing the creative work itself.",
    backstageCopy:
      "Guide is app-owned onboarding. It can consult docs and inspect lightweight context, but it is deliberately not a catch-all creative surface.",
    backstageNotes: [
      "Explains the difference between the app modes",
      "Uses docs and light inspection for troubleshooting",
      "Strongly nudges the user toward a task-specific mode",
    ],
    guardrails: [
      "No creative drafting from Guide",
      "No hidden roleplay or council work",
      "No stealth Forge execution",
    ],
    flow: [
      { label: "User Goal", detail: "Bring in a rough intention or a moment of confusion." },
      { label: "Guide Mode", detail: "Translate that intention into QuillForge terms." },
      { label: "Docs & Health", detail: "Check mode boundaries, docs, and obvious setup issues." },
      { label: "Next Move", detail: "Recommend the right room and the cleanest next step.", kind: "terminal" },
    ],
    sampleOutput:
      "Start in Writer if you want grounded chapter prose, Roleplay if you want to act inside the scene, Council if you want competing takes, Research if you want sources, and Forge if you want an explicit long-form pipeline.",
  },
  writer: {
    id: "writer",
    label: "Writer",
    badge: "Draft & Review",
    family: "Scene Engine",
    accent: "#ff9d57",
    promise: "Turns an idea into grounded prose you can review, revise, or reject.",
    feelsLike: "A story room that hands you pages instead of vibes.",
    whenToUse: "When you want chapter prose, scene drafting, or canon-aware revision inside a project file.",
    returns: "A reviewable draft, with accept and reject handled by the app.",
    userPrompt:
      "Draft the opening chapter where Mara descends into the drowned cathedral and hears the bell under the water.",
    visibleTitle: "Reviewable draft",
    visibleResult:
      "Writer returns grounded prose for review. The app handles accept, reject, regenerate, and saving so the mode can stay focused on the scene itself.",
    backstageCopy:
      "Writer does not freehand from the top. The request routes through the scene pipeline so canon checks, state updates, and final prose rendering each have a clear owner.",
    backstageNotes: [
      "Narrative Director decides the next beat",
      "Librarian checks canon before specific claims land",
      "Story and narrative state update between turns",
      "Prose Writer renders the visible draft prose",
    ],
    guardrails: [
      "No top-level prose shortcut for canon-sensitive drafting",
      "No direct file write for the normal review flow",
      "Canon corrections trigger re-grounding, not local patching",
    ],
    flow: [
      { label: "User Request", detail: "Ask for a scene, chapter beat, or revision." },
      { label: "Narrative Director", detail: "Decide what happens next and what must stay true." },
      {
        label: "Canon & Session State",
        detail: "Ground the turn before prose appears.",
        kind: "cluster",
        items: ["Librarian", "Story state", "Narrative notes"],
      },
      { label: "Prose Writer", detail: "Render the scene in the chosen voice." },
      { label: "Draft for Review", detail: "Return prose for accept, reject, or revision.", kind: "terminal" },
    ],
    sampleOutput:
      "Mara lowered the lantern until its gold turned green in the flooded nave. Far below the cracked saints and floating wax, a bell tolled once from beneath the cathedral floor, and every mooring chain in the city answered it.",
  },
  roleplay: {
    id: "roleplay",
    label: "Roleplay",
    badge: "Live Scene",
    family: "Scene Engine",
    accent: "#8db8ff",
    promise: "Keeps the user inside the scene while QuillForge tracks lore, character context, and consequences backstage.",
    feelsLike: "A playable scene with a hidden stage crew keeping continuity intact.",
    whenToUse: "When you want to act inside the story, not outline it from above.",
    returns: "An in-scene reply that continues the moment and auto-appends to the session file.",
    userPrompt: "I wedge the bronze gate with my spear and call the prince by his childhood name.",
    visibleTitle: "In-scene reply",
    visibleResult:
      "Roleplay answers as the scene, not as a conductor. The visible response stays diegetic unless the user explicitly asks for commentary or a tool failure must be disclosed.",
    backstageCopy:
      "Roleplay shares the grounded scene pipeline with Writer, but its output is live scene prose and it can fold in dice or mechanics when chance enters the turn.",
    backstageNotes: [
      "Narrative Director controls reactions and pacing",
      "Lore and character context stay active the whole time",
      "Dice or mechanics can interrupt for explicit chance events",
      "Visible output stays inside the scene itself",
    ],
    guardrails: [
      "No assistant framing unless the user asks for it",
      "No top-level prose shortcut from the mode surface",
      "Session-established facts stay authoritative until contradicted",
    ],
    flow: [
      { label: "User Turn", detail: "Act inside the story world." },
      { label: "Narrative Director", detail: "Interpret the move and stage the response." },
      {
        label: "Context Pressure",
        detail: "Pull in the live context that changes the turn.",
        kind: "cluster",
        items: ["Lore", "Character cards", "Dice when needed"],
      },
      { label: "Prose Writer", detail: "Render the beat the player actually sees." },
      { label: "Live Scene Reply", detail: "Append the scene forward in real time.", kind: "terminal" },
    ],
    sampleOutput:
      "The gate screamed open by a finger's breadth. Cold water breathed through the seam, and somewhere in the black below, a young man's laugh broke apart into the first syllable of your name.",
  },
  lore: {
    id: "lore",
    label: "Lore Builder",
    badge: "Canon Workshop",
    family: "World Engine",
    accent: "#8ae7c1",
    promise: "Turns rough world-building into durable lore files the Librarian can retrieve later.",
    feelsLike: "A reference desk that can draft the encyclopedia entry with you.",
    whenToUse: "When you want to create, research, compare, or revise canon material before it becomes a source of truth.",
    returns: "Drafted or saved markdown lore entries with clear source boundaries.",
    userPrompt: "Help me create a faction file for the Silverwatch and save it in the active lore set.",
    visibleTitle: "Lore draft or saved file",
    visibleResult:
      "Lore Builder checks existing context, separates character cards and session facts from lore documents, and saves approved markdown where the Librarian can use it.",
    backstageCopy:
      "Lore Builder exists so Guide does not become a hidden all-purpose editor. It owns the conversation-to-lore-file workflow directly.",
    backstageNotes: [
      "query_context labels where facts came from",
      "query_lore searches existing lore documents",
      "web_search can gather real-world reference",
      "save_lore_file writes approved markdown into the active lore set",
    ],
    guardrails: [
      "Does not treat web search as fictional canon by itself",
      "Does not write story prose or roleplay turns",
      "Does not save uncertain facts without user approval",
    ],
    flow: [
      { label: "World-Building Goal", detail: "Bring a faction, place, rule, timeline, or open canon question." },
      {
        label: "Source Check",
        detail: "Separate lore docs from character cards, session canon, and real-world reference.",
        kind: "cluster",
        items: ["Lore docs", "Context", "Web reference"],
      },
      { label: "Lore Draft", detail: "Shape the material into compact markdown." },
      { label: "User Approval", detail: "Confirm canon before saving uncertain or researched facts." },
      { label: "Saved Lore File", detail: "Write to the active lore set for future retrieval.", kind: "terminal" },
    ],
    sampleOutput:
      "Silverwatch is now drafted as a lore entry: oath, territory, public face, private fractures, and open hooks. Once approved, it saves under the active lore set for the Librarian.",
  },
  council: {
    id: "council",
    label: "Council",
    badge: "Multi-Voice",
    family: "Thinking Engine",
    accent: "#f5d26a",
    promise: "Fans one question out to multiple advisors, then turns the disagreement into something useful.",
    feelsLike: "A writers' room where every chair talks back.",
    whenToUse: "When you want critique, options, or competing creative viewpoints before committing to a direction.",
    returns: "A synthesis that makes agreement, tension, and standout ideas easy to see.",
    userPrompt: "Should the missing prince be a martyr, a fraud, or a living hostage?",
    visibleTitle: "Synthesis",
    visibleResult:
      "Council mode returns a balanced synthesis that makes agreement, tension, and unusual ideas legible instead of pretending there was only one obvious answer.",
    backstageCopy:
      "Council uses an Assistant-shaped surface to frame the question, then runs the council in parallel before synthesizing the result for the user.",
    backstageNotes: [
      "Assistant clarifies the advisory question",
      "run_council fans the prompt out in parallel",
      "Each member answers from its own viewpoint",
      "The surface returns a synthesis, not a fake solo answer",
    ],
    guardrails: [
      "Does not impersonate every advisor inline",
      "Does not skip the council for substantive questions",
      "Does not turn into a file-management mode",
    ],
    flow: [
      { label: "User Question", detail: "Ask for critique, options, or strategic pressure-testing." },
      { label: "Assistant Surface", detail: "Frame the question and launch the council." },
      {
        label: "Parallel Advisors",
        detail: "Multiple perspectives fire at once.",
        kind: "cluster",
        items: ["Dramaturg", "Worldbuilder", "Story critic"],
      },
      { label: "Synthesis", detail: "Bring the friction back as one useful answer.", kind: "terminal" },
    ],
    sampleOutput:
      "The council splits cleanly: one advisor loves the prince as a political myth the city invented, another wants him alive so the plot can chase him, and a third argues the strongest version is a hostage whose legend has outrun the truth.",
  },
  research: {
    id: "research",
    label: "Research",
    badge: "Source & Save",
    family: "Thinking Engine",
    accent: "#9ef07a",
    promise: "Breaks a broad question into topics, investigates them in parallel, and saves the full findings.",
    feelsLike: "A newsroom board where every string leads to a saved file.",
    whenToUse: "When the answer should be sourced, structured, and reusable later instead of improvised from memory.",
    returns: "A briefing in chat plus markdown findings saved to the research project.",
    userPrompt: "Research flood myths, cathedral acoustics, and ritual tides I can steal for the drowned city.",
    visibleTitle: "Briefing plus files",
    visibleResult:
      "Research mode gives you a synthesized briefing in chat and saves deeper markdown findings under the research project so the work survives past the moment.",
    backstageCopy:
      "Research also uses the Assistant surface, but instead of advisors it launches a pool of research agents that run multi-step searches and write their own notes.",
    backstageNotes: [
      "Assistant splits broad requests into focused topics",
      "ResearchPool runs multiple research agents in parallel",
      "Each agent searches, refines, and writes findings",
      "Chat returns the synthesis while markdown files persist",
    ],
    guardrails: [
      "Does not fake sourced answers from intuition",
      "Does not browse ad hoc from the surface when the tool should run",
      "Discloses missing project setup or tool failures plainly",
    ],
    flow: [
      { label: "User Question", detail: "Bring in a factual or source-hungry request." },
      { label: "Assistant Surface", detail: "Split the work into researchable topics." },
      {
        label: "Research Pool",
        detail: "Dedicated topic agents work in parallel.",
        kind: "cluster",
        items: ["Flood myths", "Bell acoustics", "Tide rituals"],
      },
      { label: "Search & Write Loops", detail: "Agents search, refine, and write markdown notes." },
      { label: "Saved Findings", detail: "Keep the deep work under the project directory." },
      { label: "Briefing", detail: "Return the user-facing synthesis in chat.", kind: "terminal" },
    ],
    sampleOutput:
      "Three threads emerged: deluge myths tied to broken oaths, submerged bell chambers that can throw sound through stone, and real ritual calendars built around extreme spring tides. The detailed notes are saved for reuse.",
  },
  games: {
    id: "games",
    label: "Games",
    badge: "Typed Table",
    family: "Rules Engine",
    accent: "#77d7ff",
    promise: "Runs social games through typed actions, participant feeds, and rules-engine authority.",
    feelsLike: "A game table with private notes, legal moves, and a visible public log.",
    whenToUse: "When you want a structured social game where agents can be players but the model never becomes the game master.",
    returns: "Game status, stage, public feed, private player information, legal actions, roster, and host controls.",
    userPrompt: "Start a village social deduction game and show me my legal actions.",
    visibleTitle: "Typed game table",
    visibleResult:
      "Games mode shows status, stage, public feed, private player projection, pending actions, roster, and controls from typed game endpoints instead of asking chat to infer rules from narration.",
    backstageCopy:
      "The rules engine owns gameplay facts. QuillForge owns the game workspace, templates, participant communication, agent turns, memory summaries, and prompt cursors around that engine boundary.",
    backstageNotes: [
      "Templates choose a registered rules-engine module",
      "Game endpoints expose typed public and player projections",
      "Agent players submit structured choices through the bridge",
      "The UI posts actions and messages through game endpoints only",
    ],
    guardrails: [
      "No hidden game-master decisions from chat",
      "No hidden-info inference from narrator text",
      "No lore, writer, forge, council, or research tools during gameplay",
    ],
    flow: [
      { label: "Template", detail: "Choose a saved game setup and seed." },
      { label: "Rules Engine", detail: "Commit typed facts, stages, pending inputs, and visible projections." },
      { label: "Table Workspace", detail: "Show public feed, private info, roster, and legal controls." },
      { label: "Agent Players", detail: "Use bounded provider calls to choose from legal actions only." },
      { label: "Journal", detail: "Persist complete gameplay and communication records.", kind: "terminal" },
    ],
    sampleOutput:
      "The game is waiting on your vote. The public feed shows the accusation, your private panel shows the legal choices, and the roster marks which participants are agents or human players.",
  },
  forge: {
    id: "forge",
    label: "Forge",
    badge: "Operate the Machine",
    family: "Production Pipeline",
    accent: "#ff7a90",
    promise: "Moves a project through explicit stages instead of hiding the process inside chat.",
    feelsLike: "Mission control for a long-form fiction pipeline.",
    whenToUse: "When you want planning, design, writing, review, and assembly to stay visible as separate stages.",
    returns: "Pipeline status, commands, checkpoints, and stage-owned output.",
    userPrompt: "/forge start drowned-cathedral",
    visibleTitle: "Pipeline status",
    visibleResult:
      "Forge chat explains what stage you are in and what command comes next, while planning, design, writing, review, and assembly stay explicit pipeline work rather than invisible chat magic.",
    backstageCopy:
      "Forge is not a secret second writer. Commands and services own the stages, which keeps long-form generation testable, reviewable, and easier to trust.",
    backstageNotes: [
      "Commands start or inspect the pipeline",
      "Planning builds the outline and chapter briefs",
      "Design refines the bible and scaffolding",
      "Writing drafts chapters, Review scores them, Assembly packages the result",
    ],
    guardrails: [
      "No hidden planning from casual chat",
      "No manual manifest hacking",
      "Redirects scene work back to Writer or Roleplay",
    ],
    flow: [
      { label: "User Commands", detail: "Operate the pipeline directly." },
      { label: "Planning", detail: "Build outline, style guide, bible, and briefs." },
      { label: "Design", detail: "Refine the scaffolding before heavy drafting." },
      { label: "Writing", detail: "Draft the chapters owned by the stage." },
      { label: "Review", detail: "Gate quality and request revisions where needed." },
      { label: "Assembly", detail: "Package the approved work into a coherent artifact.", kind: "terminal" },
    ],
    sampleOutput:
      "Planning locked the premise. Design tightened the world bible. Writing queued chapter drafts. Review gates quality before Assembly turns the project into a coherent manuscript artifact.",
  },
};

const SCENE_PIPELINE_SPECS: Record<ScenePipelineMode, ScenePipelineSpec> = {
  writer: {
    label: "Writer",
    badge: "Draft & Review",
    accent: TOUR_MODES.writer.accent,
    intro: "Writer mode uses the grounded scene pipeline, then hands the result back as reviewable draft prose.",
    userLabel: "User Prompt",
    userLine: "Draft the opening chapter where Mara hears the drowned bell below the nave.",
    returnLabel: "Draft for Review",
    returnLine: "Return grounded prose for accept, reject, or revision. The app owns save and review actions.",
    outcome: "You are not talking straight to the prose writer. The prose writer renders the final draft after Narrative Director has already decided the beat and grounded it.",
    appNote: "Writer mode ends in a review loop, not a direct file write.",
  },
  roleplay: {
    label: "Roleplay",
    badge: "Live Scene",
    accent: TOUR_MODES.roleplay.accent,
    intro: "Roleplay mode uses the same scene pipeline, but the result comes back as live in-scene prose instead of a reviewable chapter draft.",
    userLabel: "User Turn",
    userLine: "I wedge the bronze gate with my spear and call the prince by his childhood name.",
    returnLabel: "Live Scene Reply",
    returnLine: "Return only the in-scene response and append the beat forward for the live session.",
    outcome: "You still are not talking directly to the prose writer. The prose writer renders what the player sees after Narrative Director has staged reactions, canon checks, and scene continuity.",
    appNote: "Roleplay mode ends in scene continuation, not a detached assistant answer.",
  },
};

const PROVIDER_PRINCIPLES: ProviderPrinciple[] = [
  {
    title: "Spend for voice",
    body: "If the agent is writing the words your reader will actually feel, premium quality tends to matter more.",
    emphasis: "Prose Writer is the clearest place to use your most impressive writing model.",
  },
  {
    title: "Save on retrieval",
    body: "Some agents are mostly checking, sorting, or bringing back grounded material instead of trying to sound beautiful.",
    emphasis: "Librarian often benefits more from speed, price, and reliability than from fireworks.",
  },
  {
    title: "Separate the jobs",
    body: "Planner, drafter, reviewer, and researcher do not all need the same temperament or price point.",
    emphasis: "A strong team often works better than one giant model doing every role.",
  },
];

const PROVIDER_ROLE_GUIDES: ProviderRoleGuide[] = [
  {
    agent: "Orchestrator / Assistant",
    recommendation: "Use a dependable model that is good at routing, tool use, and staying on task.",
    why: "This is the front desk. It needs judgment and consistency more than showy prose.",
  },
  {
    agent: "Narrative Director",
    recommendation: "Use a model with strong scene judgment and continuity, even if it is not your most lyrical one.",
    why: "Its job is deciding the beat, checking what must stay true, and shaping the handoff to prose.",
  },
  {
    agent: "Prose Writer",
    recommendation: "This is where a frontier-quality, wordy, expensive model can really pay off.",
    why: "It writes the final text the user actually reads, so style and expressive range matter here.",
  },
  {
    agent: "Librarian",
    recommendation: "A cheaper, faster, grounded model is often the smart choice.",
    why: "The librarian is usually doing lookup and synthesis work, not trying to write luminous final prose.",
  },
  {
    agent: "Research / Delegate Technical",
    recommendation: "Favor practical, affordable models that can handle factual work without wasting your premium budget.",
    why: "These roles often operate in the background or across multiple queries, so throughput matters.",
  },
  {
    agent: "Forge Planner / Writer / Reviewer",
    recommendation: "Treat these as three separate jobs if you can.",
    why: "You may want one model that plans clearly, one that drafts boldly, and a different one that judges more strictly.",
  },
];

function TourModeIcon({
  mode,
  frameClassName = "tour-mode-icon-frame",
  iconClassName = "tour-mode-icon",
}: {
  mode: Mode;
  frameClassName?: string;
  iconClassName?: string;
}) {
  return (
    <span className={frameClassName} aria-hidden="true">
      <img src={MODE_ICON_PATHS[mode]} alt="" className={iconClassName} />
    </span>
  );
}

export default function StoryTourPage() {
  const [activeMode, setActiveMode] = useState<Mode>("guide");
  const [scenePipelineMode, setScenePipelineMode] = useState<ScenePipelineMode>("writer");
  const sharedSearch = window.location.search;
  const appHref = `/${sharedSearch}`;
  const tourHref = `/tour${sharedSearch}`;

  useEffect(() => {
    const previousTitle = document.title;
    document.title = "QuillForge Tour";
    return () => {
      document.title = previousTitle;
    };
  }, []);

  const effectiveScenePipelineMode: ScenePipelineMode =
    activeMode === "writer" || activeMode === "roleplay"
      ? activeMode
      : scenePipelineMode;

  const active = TOUR_MODES[activeMode];
  const rootStyle = { "--mode-accent": active.accent } as CSSProperties;
  const scenePipeline = SCENE_PIPELINE_SPECS[effectiveScenePipelineMode];
  const sceneStyle = { "--scene-accent": scenePipeline.accent } as CSSProperties;

  function handleModeSelect(mode: Mode) {
    setActiveMode(mode);
  }

  return (
    <div className="story-tour" style={rootStyle}>
      <header className="tour-nav">
        <a className="tour-brand" href={tourHref}>
          <span className="tour-brand-mark">QF</span>
          <span>
            <strong>QuillForge Tour</strong>
            <span className="tour-brand-subtitle">modes, agents, and handoffs</span>
          </span>
        </a>

        <nav className="tour-nav-links" aria-label="Tour sections">
          <a href="#mode-lab">Mode Lab</a>
          <a href="#backstage">Backstage</a>
          <a href="#seed">One Seed</a>
          <a href="#provider-casting">Providers</a>
          <a href="#scene-pipeline">Scene Loop</a>
        </nav>

        <div className="tour-nav-actions">
          <a className="tour-primary-button" href={appHref}>
            Open App
          </a>
        </div>
      </header>

      <main className="tour-main">
        <section className="tour-hero">
          <div className="tour-hero-layout">
            <div className="tour-copy">
              <div className="tour-intro-copy">
                <h1 className="tour-display">QuillForge is a story studio with different rooms.</h1>

                <p className="tour-lead">
                  The world stays the same. What changes is which crew wakes up backstage, what kind of work they do,
                  and what comes back to you.
                </p>
              </div>
            </div>

            <aside className="tour-hero-diagram" aria-label="How QuillForge shapes a response">
              <div className="tour-hero-diagram-copy">
                <span className="tour-panel-eyebrow">How A Reply Gets Made</span>
                <p>
                  You start by talking to a mode. That mode wakes the right backstage crew, and Prose Writer turns the
                  grounded result into the words you finally see.
                </p>
              </div>

              <div className="tour-lifecycle-grid">
                <article className="tour-lifecycle-node is-user">
                  <span className="tour-lifecycle-label">User</span>
                  <strong>You</strong>
                  <p>Bring in the goal, question, scene turn, or request.</p>
                </article>

                <div className="tour-lifecycle-link is-forward is-prompt">
                  <span>Prompt</span>
                </div>

                <article className="tour-lifecycle-node is-mode">
                  <span className="tour-lifecycle-label">Mode</span>
                  <strong>Choose the room</strong>
                  <p>Guide, Writer, Roleplay, Lore Builder, Council, Research, or Forge frames the job.</p>
                </article>

                <div className="tour-lifecycle-link is-forward is-dispatch">
                  <span>Dispatch</span>
                </div>

                <article className="tour-lifecycle-node is-agents">
                  <span className="tour-lifecycle-label">Mode Processing</span>
                  <strong>Agent crew</strong>
                  <p>Specialists ground, route, check context, and decide what kind of response should come back.</p>
                </article>

                <div className="tour-lifecycle-link is-backward is-output">
                  <span>Output</span>
                </div>

                <article className="tour-lifecycle-node is-prose">
                  <span className="tour-lifecycle-label">Prose Writer</span>
                  <strong>Visible wording</strong>
                  <p>The final text gets rendered only after the upstream mode work is already done.</p>
                </article>

                <div className="tour-lifecycle-link is-backward is-draft">
                  <span>Handoff</span>
                </div>
              </div>
            </aside>
          </div>
        </section>

        <section className="tour-section" id="mode-lab">
          <div className="tour-section-heading">
            <div>
              <span className="tour-panel-eyebrow">Mode Lab</span>
              <h2>What the user feels, versus what QuillForge is actually doing</h2>
            </div>
            <p>
              This is the part people usually miss in text. The visible experience is simple on purpose. The agent
              choreography underneath changes by mode.
            </p>
          </div>

          <div className="tour-mode-rail" role="tablist" aria-label="Modes">
            {MODE_ORDER.map((mode) => {
              const spec = TOUR_MODES[mode];
              const buttonStyle = { "--card-accent": spec.accent } as CSSProperties;

              return (
                <button
                  key={mode}
                  className={`tour-mode-pill ${mode === activeMode ? "is-active" : ""}`}
                  style={buttonStyle}
                  onClick={() => handleModeSelect(mode)}
                >
                  <span className="tour-mode-pill-row">
                    <TourModeIcon mode={mode} frameClassName="tour-mode-icon-frame tour-mode-icon-frame-pill" />
                    <span className="tour-mode-pill-copy">
                      <span>{spec.label}</span>
                      <small>{spec.badge}</small>
                    </span>
                  </span>
                </button>
              );
            })}
          </div>

          <div className="tour-lab-grid">
            <article className="tour-console-card">
              <div className="tour-console-topbar">
                <span />
                <span />
                <span />
              </div>

              <div className="tour-chat-stack">
                <div className="tour-chat-bubble is-user">
                  <span className="tour-chat-role">User</span>
                  <p>{active.userPrompt}</p>
                </div>

                <div className="tour-chat-bubble is-assistant">
                  <span className="tour-chat-role">{active.visibleTitle}</span>
                  <p>{active.visibleResult}</p>
                </div>
              </div>
            </article>

            <article className="tour-detail-card">
              <span className="tour-panel-eyebrow">Why This Room Exists</span>
              <h3>{active.promise}</h3>

              <dl className="tour-detail-grid">
                <div>
                  <dt>Family</dt>
                  <dd>{active.family}</dd>
                </div>
                <div>
                  <dt>Best For</dt>
                  <dd>{active.whenToUse}</dd>
                </div>
                <div>
                  <dt>Feels Like</dt>
                  <dd>{active.feelsLike}</dd>
                </div>
                <div>
                  <dt>Comes Back As</dt>
                  <dd>{active.returns}</dd>
                </div>
              </dl>
            </article>
          </div>
        </section>

        <section className="tour-section" id="backstage">
          <div className="tour-section-heading">
            <div>
              <span className="tour-panel-eyebrow">Backstage</span>
              <h2>Who actually wakes up behind the curtain</h2>
            </div>
            <p>{active.backstageCopy}</p>
          </div>

          <div className="tour-flow-shell">
            <div className="tour-flow-track">
              {active.flow.map((step, index) => (
                <div className="tour-flow-fragment" key={`${active.id}-${step.label}`}>
                  <article className={`tour-flow-step ${step.kind ?? ""}`}>
                    <span className="tour-flow-index">{String(index + 1).padStart(2, "0")}</span>
                    <h3>{step.label}</h3>
                    <p>{step.detail}</p>
                    {step.items ? (
                      <div className="tour-step-items">
                        {step.items.map((item) => (
                          <span key={item}>{item}</span>
                        ))}
                      </div>
                    ) : null}
                  </article>
                  {index < active.flow.length - 1 ? <div className="tour-flow-arrow" aria-hidden="true" /> : null}
                </div>
              ))}
            </div>
          </div>

          <div className="tour-backstage-grid">
            <article className="tour-detail-card">
              <span className="tour-panel-eyebrow">What This Mode Protects</span>
              <ul className="tour-list">
                {active.backstageNotes.map((note) => (
                  <li key={note}>{note}</li>
                ))}
              </ul>
            </article>

            <article className="tour-detail-card">
              <span className="tour-panel-eyebrow">What It Refuses To Be</span>
              <ul className="tour-list">
                {active.guardrails.map((guardrail) => (
                  <li key={guardrail}>{guardrail}</li>
                ))}
              </ul>
            </article>
          </div>
        </section>

        <section className="tour-section" id="seed">
          <div className="tour-section-heading">
            <div>
              <span className="tour-panel-eyebrow">One Seed, Many Paths</span>
              <h2>The same premise sounds radically different depending on the room</h2>
            </div>
            <p>
              This is the punchline the page is trying to teach. QuillForge is not “one chat with extra buttons.”
              Every mode turns the same material into a different kind of output.
            </p>
          </div>

          <div className="tour-seed-grid">
            {MODE_ORDER.map((mode) => {
              const spec = TOUR_MODES[mode];
              const cardStyle = { "--card-accent": spec.accent } as CSSProperties;

              return (
                <button
                  key={mode}
                  className={`tour-seed-card ${mode === activeMode ? "is-active" : ""}`}
                  style={cardStyle}
                  onClick={() => handleModeSelect(mode)}
                >
                  <div className="tour-seed-card-head">
                    <TourModeIcon mode={mode} frameClassName="tour-mode-icon-frame tour-mode-icon-frame-seed" />
                    <div>
                      <span className="tour-seed-badge">{spec.label}</span>
                      <strong>{spec.badge}</strong>
                    </div>
                  </div>
                  <p>{spec.sampleOutput}</p>
                </button>
              );
            })}
          </div>

          <article className="tour-sample-panel">
            <div className="tour-sample-header">
              <div className="tour-sample-title">
                <TourModeIcon mode={activeMode} frameClassName="tour-mode-icon-frame tour-mode-icon-frame-sample" />
                <div>
                  <span className="tour-panel-eyebrow">Spotlighted Output</span>
                  <h3>{active.label}</h3>
                </div>
              </div>
              <div className="tour-sample-family">{active.family}</div>
            </div>

            <blockquote>{active.sampleOutput}</blockquote>
            <p>{active.whenToUse}</p>
          </article>
        </section>

        <section className="tour-provider-panel" id="provider-casting">
          <div className="tour-section-heading">
            <div>
              <span className="tour-panel-eyebrow">Provider Casting</span>
              <h2>That provider assignment panel is really a casting board for the whole team</h2>
            </div>
            <p>
              QuillForge lets you point different agents at different provider aliases. In plain English, that means
              you do not have to spend the same amount of money or expect the same personality from every job.
            </p>
          </div>

          <div className="tour-provider-hero">
            <article className="tour-provider-intro">
              <span className="tour-panel-eyebrow">The Mental Model</span>
              <h3>You are not picking one brain for the whole app.</h3>
              <p>
                You are assigning a different worker to a different role. A premium frontier model might be perfect for
                the final prose. A cheaper faster model might be completely fine for lore lookup, research chores, or
                other background work.
              </p>
              <p className="tour-provider-note">
                In the Provider Manager, those dropdowns are the app's way of saying: which worker should do this job?
              </p>
            </article>

            <div className="tour-provider-principles">
              {PROVIDER_PRINCIPLES.map((principle) => (
                <article className="tour-provider-principle" key={principle.title}>
                  <h3>{principle.title}</h3>
                  <p>{principle.body}</p>
                  <strong>{principle.emphasis}</strong>
                </article>
              ))}
            </div>
          </div>

          <div className="tour-provider-roster">
            {PROVIDER_ROLE_GUIDES.map((role) => (
              <article className="tour-provider-role" key={role.agent}>
                <div>
                  <span className="tour-panel-eyebrow">Agent</span>
                  <h3>{role.agent}</h3>
                </div>
                <div>
                  <span className="tour-panel-eyebrow">Good Fit</span>
                  <p>{role.recommendation}</p>
                </div>
                <div>
                  <span className="tour-panel-eyebrow">Why</span>
                  <p>{role.why}</p>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="tour-scene-panel" id="scene-pipeline" style={sceneStyle}>
          <div className="tour-section-heading">
            <div>
              <span className="tour-panel-eyebrow">Grounded Scene Loop</span>
              <h2>In Writer and Roleplay, you are not talking straight to Prose Writer</h2>
            </div>
            <p>
              The user-facing surface feels simple, but the response is assembled through a pipeline with clear owners.
              Narrative Director decides the beat. Prose Writer renders the beat. Librarian and session state help
              ground it before the text comes back.
            </p>
          </div>

          <div className="tour-scene-toggle" role="tablist" aria-label="Scene pipeline modes">
            {(Object.keys(SCENE_PIPELINE_SPECS) as ScenePipelineMode[]).map((mode) => {
              const spec = SCENE_PIPELINE_SPECS[mode];
              const buttonStyle = { "--card-accent": spec.accent } as CSSProperties;

              return (
                <button
                  key={mode}
                  className={`tour-scene-pill ${effectiveScenePipelineMode === mode ? "is-active" : ""}`}
                  style={buttonStyle}
                  onClick={() => setScenePipelineMode(mode)}
                  aria-pressed={effectiveScenePipelineMode === mode}
                >
                  <span className="tour-scene-pill-row">
                    <TourModeIcon mode={mode} frameClassName="tour-mode-icon-frame tour-mode-icon-frame-scene" />
                    <span className="tour-scene-pill-copy">
                      <span>{spec.label}</span>
                      <small>{spec.badge}</small>
                    </span>
                  </span>
                </button>
              );
            })}
          </div>

          <div className="tour-scene-shell">
            <div className="tour-scene-track">
              <article className="tour-scene-node">
                <span className="tour-flow-index">01</span>
                <h3>{scenePipeline.userLabel}</h3>
                <p>{scenePipeline.userLine}</p>
              </article>

              <div className="tour-scene-arrow" aria-hidden="true" />

              <article className="tour-scene-node is-owner">
                <span className="tour-flow-index">02</span>
                <h3>Narrative Director</h3>
                <p>Owns what happens next, checks what must stay true, and decides what kind of prose should come back.</p>
              </article>

              <article className="tour-scene-loop">
                <span className="tour-panel-eyebrow">Grounding Loop</span>
                <p>The director can go back and forth here before the user ever sees a line of prose.</p>
                <div className="tour-scene-loop-items">
                  <span>Librarian</span>
                  <span>Story state</span>
                  <span>Narrative state</span>
                </div>
              </article>

              <div className="tour-scene-arrow" aria-hidden="true" />

              <article className="tour-scene-node">
                <span className="tour-flow-index">03</span>
                <h3>Prose Writer</h3>
                <p>Renders the final visible text once the beat is already grounded and directed.</p>
              </article>

              <div className="tour-scene-arrow" aria-hidden="true" />

              <article className="tour-scene-node is-return">
                <span className="tour-flow-index">04</span>
                <h3>{scenePipeline.returnLabel}</h3>
                <p>{scenePipeline.returnLine}</p>
              </article>
            </div>

            <div className="tour-scene-notes">
              <article className="tour-detail-card">
                <span className="tour-panel-eyebrow">What This Sells</span>
                <h3>{scenePipeline.label} is a grounded scene surface, not a raw prose endpoint.</h3>
                <p>{scenePipeline.intro}</p>
              </article>

              <article className="tour-detail-card">
                <span className="tour-panel-eyebrow">Important Mental Model</span>
                <p>{scenePipeline.outcome}</p>
                <p className="tour-scene-note">{scenePipeline.appNote}</p>
              </article>
            </div>
          </div>

          <div className="tour-footer-actions">
            <a className="tour-primary-button" href={appHref}>
              Back to QuillForge
            </a>
          </div>
        </section>
      </main>
    </div>
  );
}
