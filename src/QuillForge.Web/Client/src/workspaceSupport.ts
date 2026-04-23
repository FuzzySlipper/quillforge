import type { Message } from "./types";

function isWriterProseMessage(message: Message): boolean {
  return (
    message.role === "assistant"
    && (message.responseType === "prose" || message.responseType === "prose_pending")
  );
}

export function formatStoryTarget(
  project: string | null | undefined,
  file: string | null | undefined,
): string | null {
  return project && file ? `story/${project}/${file}` : null;
}

export function getWriterProseSummary(messages: Message[]): {
  latestProse: Message | null;
  proseCount: number;
} {
  let latestProse: Message | null = null;
  let proseCount = 0;

  for (const message of messages) {
    if (!isWriterProseMessage(message)) {
      continue;
    }

    proseCount += 1;
    latestProse = message;
  }

  return { latestProse, proseCount };
}
