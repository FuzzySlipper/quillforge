interface ShellIconProps {
  name:
    | "plus"
    | "stack"
    | "panel"
    | "user"
    | "settings"
    | "palette"
    | "context"
    | "spark"
    | "chevron-left"
    | "chevron-right";
  className?: string;
}

export default function ShellIcon({ name, className }: ShellIconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className ?? "h-[18px] w-[18px]"}
      aria-hidden="true"
    >
      {name === "plus" && (
        <>
          <path d="M12 5v14" />
          <path d="M5 12h14" />
        </>
      )}
      {name === "stack" && (
        <>
          <path d="M6 7h12" />
          <path d="M6 12h12" />
          <path d="M6 17h12" />
        </>
      )}
      {name === "panel" && (
        <>
          <rect x="4" y="5" width="16" height="14" rx="2" />
          <path d="M9 5v14" />
        </>
      )}
      {name === "user" && (
        <>
          <circle cx="12" cy="9" r="3.5" />
          <path d="M5 19c.8-3 3.5-4.8 7-4.8s6.2 1.8 7 4.8" />
        </>
      )}
      {name === "settings" && (
        <>
          <circle cx="12" cy="12" r="3" />
          <path d="M12 4v2.2" />
          <path d="M12 17.8V20" />
          <path d="M4 12h2.2" />
          <path d="M17.8 12H20" />
          <path d="M6.3 6.3 7.8 7.8" />
          <path d="m16.2 16.2 1.5 1.5" />
          <path d="m16.2 7.8 1.5-1.5" />
          <path d="m6.3 17.7 1.5-1.5" />
        </>
      )}
      {name === "palette" && (
        <>
          <path d="M12 4a8 8 0 1 0 0 16h1a2 2 0 0 0 0-4h-1a2 2 0 0 1 0-4h2a3 3 0 0 0 0-6h-2Z" />
          <circle cx="8.5" cy="10" r="1" fill="currentColor" stroke="none" />
          <circle cx="12" cy="8.2" r="1" fill="currentColor" stroke="none" />
          <circle cx="15.4" cy="10.2" r="1" fill="currentColor" stroke="none" />
        </>
      )}
      {name === "context" && (
        <>
          <circle cx="12" cy="12" r="8" />
          <path d="M12 8v5l3 2" />
        </>
      )}
      {name === "spark" && (
        <>
          <path d="M12 3v4" />
          <path d="M12 17v4" />
          <path d="M3 12h4" />
          <path d="M17 12h4" />
          <path d="m6 6 2.5 2.5" />
          <path d="M15.5 15.5 18 18" />
          <path d="m6 18 2.5-2.5" />
          <path d="M15.5 8.5 18 6" />
        </>
      )}
      {name === "chevron-left" && <path d="m14.5 6-6 6 6 6" />}
      {name === "chevron-right" && <path d="m9.5 6 6 6-6 6" />}
    </svg>
  );
}
