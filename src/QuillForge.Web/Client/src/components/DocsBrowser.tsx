import { useEffect, useState } from "react";
import { listDocsTopics, getDocTopic, searchDocs, type DocsTopic, type DocsTopicEntry, type DocsSearchResult } from "../api";
import SurfaceFrame, { type SurfaceVariant } from "./SurfaceFrame";

interface DocsBrowserProps {
  open: boolean;
  onClose: () => void;
  variant?: SurfaceVariant;
}

export default function DocsBrowser({
  open,
  onClose,
  variant = "overlay",
}: DocsBrowserProps) {
  const [topics, setTopics] = useState<DocsTopic[]>([]);
  const [selected, setSelected] = useState<DocsTopicEntry | null>(null);
  const [loading, setLoading] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState<DocsSearchResult[] | null>(null);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    if (!open) return;
    refresh();
  }, [open]);

  async function refresh() {
    setLoading(true);
    try {
      const data = await listDocsTopics();
      setTopics(data.topics ?? []);
    } catch {
      setTopics([]);
    } finally {
      setLoading(false);
    }
  }

  async function handleSelect(slug: string) {
    setLoading(true);
    try {
      const entry = await getDocTopic(slug);
      setSelected(entry);
      setSearchResults(null);
      setSearchQuery("");
    } catch {
      setSelected(null);
    } finally {
      setLoading(false);
    }
  }

  async function handleSearch() {
    const q = searchQuery.trim();
    if (!q) {
      setSearchResults(null);
      return;
    }
    setSearching(true);
    try {
      const data = await searchDocs(q);
      setSearchResults(data.results ?? []);
      setSelected(null);
    } catch {
      setSearchResults([]);
    } finally {
      setSearching(false);
    }
  }

  function handleBack() {
    setSelected(null);
    setSearchResults(null);
  }

  return (
    <SurfaceFrame open={open} onClose={onClose} title="Documentation" variant={variant}>
      <div className="flex h-full min-h-0 flex-col gap-3">
        {/* Search bar */}
        <div className="flex gap-2">
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                void handleSearch();
              }
            }}
            placeholder="Search docs..."
            className="qf-shell-input flex-1 text-sm"
          />
          <button
            type="button"
            onClick={() => void handleSearch()}
            disabled={searching}
            className="qf-shell-card px-3 py-1.5 text-sm transition-colors hover:border-accent/40 hover:text-text disabled:opacity-60"
          >
            {searching ? "..." : "Search"}
          </button>
          {(selected || searchResults !== null) && (
            <button
              type="button"
              onClick={handleBack}
              className="qf-shell-card px-3 py-1.5 text-sm transition-colors hover:border-accent/40 hover:text-text"
            >
              Back
            </button>
          )}
        </div>

        {/* Loading */}
        {loading && <p className="text-sm text-text-muted">Loading...</p>}

        {/* Selected topic */}
        {selected && (
          <div className="min-h-0 flex-1 overflow-y-auto">
            <h3 className="text-base font-semibold text-text">{selected.name}</h3>
            <p className="mt-1 text-sm text-text-muted">{selected.summary}</p>
            <div className="mt-4 space-y-3 text-sm leading-6 text-text">
              {selected.content.split("\n").map((line, i) => {
                const trimmed = line.trim();
                if (trimmed.startsWith("# ")) {
                  return <h1 key={i} className="text-lg font-semibold text-text">{trimmed.slice(2)}</h1>;
                }
                if (trimmed.startsWith("## ")) {
                  return <h2 key={i} className="text-base font-semibold text-text mt-4">{trimmed.slice(3)}</h2>;
                }
                if (trimmed.startsWith("### ")) {
                  return <h3 key={i} className="text-sm font-semibold text-text mt-3">{trimmed.slice(4)}</h3>;
                }
                if (trimmed.startsWith("- ")) {
                  return <li key={i} className="ml-4 list-disc text-text-muted">{trimmed.slice(2)}</li>;
                }
                if (trimmed.startsWith("```")) {
                  return null;
                }
                if (trimmed === "") {
                  return <div key={i} className="h-2" />;
                }
                return <p key={i} className="text-text-muted">{line}</p>;
              })}
            </div>
          </div>
        )}

        {/* Search results */}
        {searchResults !== null && !selected && (
          <div className="min-h-0 flex-1 overflow-y-auto">
            {searchResults.length === 0 ? (
              <p className="text-sm text-text-muted">No results found.</p>
            ) : (
              <div className="space-y-3">
                {searchResults.map((result) => (
                  <button
                    key={result.slug}
                    type="button"
                    onClick={() => void handleSelect(result.slug)}
                    className="qf-shell-card w-full px-3 py-3 text-left transition-colors hover:border-accent/40 hover:bg-input-bg/50"
                  >
                    <div className="text-sm font-medium text-text">{result.name}</div>
                    <div className="mt-1 space-y-1">
                      {result.snippets.map((snippet, idx) => (
                        <p key={idx} className="text-xs text-text-muted leading-relaxed">{snippet}</p>
                      ))}
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}

        {/* Topic list */}
        {!selected && searchResults === null && (
          <div className="min-h-0 flex-1 overflow-y-auto">
            {topics.length === 0 ? (
              <p className="text-sm text-text-muted">No documentation topics available.</p>
            ) : (
              <div className="space-y-2">
                {topics.map((topic) => (
                  <button
                    key={topic.slug}
                    type="button"
                    onClick={() => void handleSelect(topic.slug)}
                    className="qf-shell-card w-full px-3 py-3 text-left transition-colors hover:border-accent/40 hover:bg-input-bg/50"
                  >
                    <div className="text-sm font-medium text-text">{topic.name}</div>
                    <div className="text-xs text-text-muted mt-0.5">{topic.summary}</div>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </SurfaceFrame>
  );
}
