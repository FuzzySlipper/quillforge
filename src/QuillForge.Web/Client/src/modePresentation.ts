import type { Mode } from "./types";

export const MODE_LABELS: Record<Mode, string> = {
  guide: "Guide",
  writer: "Writer",
  roleplay: "Roleplay",
  lore: "Lore Builder",
  forge: "Forge",
  council: "Council",
  research: "Research",
  games: "Games",
};

export const MODE_DESCRIPTIONS: Record<Mode, string> = {
  guide: "Onboarding and troubleshooting surface that explains modes and helps you pick the right workflow.",
  writer: "Project-based writing with accept/reject/regenerate flow.",
  roleplay: "Chat-based roleplay with a character card.",
  lore: "Guided world-building for creating and refining lore documents used by the Librarian.",
  forge: "Command-and-pipeline control surface for Forge projects, stage runs, and status checks.",
  council: "Every message is routed through the council for multiple perspectives before synthesis.",
  research: "Multi-agent web research with parallel topic investigation and organized markdown findings.",
  games: "Typed social game table with rules-engine-owned actions, public feed, private info, and participant controls.",
};

export const MODE_ICON_PATHS: Record<Mode, string> = {
  guide: "/mode-icons/guide.svg",
  writer: "/mode-icons/writer.svg",
  roleplay: "/mode-icons/roleplay.svg",
  lore: "/mode-icons/lore.svg",
  forge: "/mode-icons/forge.svg",
  council: "/mode-icons/council.svg",
  research: "/mode-icons/research.svg",
  games: "/mode-icons/games.svg",
};
