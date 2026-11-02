const paths = {
  headphones: <><path d="M3 14v-3a9 9 0 0 1 18 0v3"/><rect x="3" y="13" width="4" height="7" rx="2"/><rect x="17" y="13" width="4" height="7" rx="2"/></>,
  book: <><path d="M12 7c-2.7-1.6-5.3-1.8-9-1.2v13c3.7-.6 6.3-.4 9 1.2 2.7-1.6 5.3-1.8 9-1.2v-13c-3.7-.6-6.3-.4-9 1.2Z"/><path d="M12 7v13"/></>,
  clock: <><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/></>,
  file: <><path d="M6 2h8l4 4v16H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2Z"/><path d="M14 2v5h5M8 12h8M8 16h6"/></>,
  chart: <><path d="M4 20V5M4 20h17M8 16l4-5 3 2 5-7"/><circle cx="8" cy="16" r="1"/><circle cx="12" cy="11" r="1"/><circle cx="15" cy="13" r="1"/><circle cx="20" cy="6" r="1"/></>,
  route: <><circle cx="5" cy="18" r="2"/><circle cx="19" cy="6" r="2"/><path d="M7 18h6a4 4 0 0 0 0-8H9a4 4 0 0 1 0-8h8"/></>,
  target: <><circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1"/></>,
  award: <><circle cx="12" cy="8" r="5"/><path d="m8 12-2 10 6-3 6 3-2-10"/></>,
  layers: <><path d="m12 3 9 5-9 5-9-5 9-5ZM3 12l9 5 9-5M3 16l9 5 9-5"/></>,
  check: <><circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/></>,
  alert: <><path d="m12 3 10 18H2L12 3Z"/><path d="M12 9v5M12 18h.01"/></>,
  flag: <><path d="M5 21V4M5 5c3-2 5 2 8 0s5 1 7 0v11c-2 1-4-2-7 0s-5-2-8 0"/></>,
  progress: <><rect x="3" y="4" width="18" height="16" rx="3"/><path d="M7 14h3l2-4 2 6 2-3h2"/></>,
  course: <><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18M8 13h5M8 17h8"/></>,
  history: <><path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5M12 7v5l4 2"/></>,
  pencil: <><path d="m4 20 4.5-1 11-11a2.1 2.1 0 0 0-3-3l-11 11L4 20ZM14 7l3 3"/></>,
  sparkle: <><path d="m12 2 1.6 6.4L20 10l-6.4 1.6L12 18l-1.6-6.4L4 10l6.4-1.6L12 2ZM19 17l.6 2.4L22 20l-2.4.6L19 23l-.6-2.4L16 20l2.4-.6L19 17Z"/></>,
  arrow: <><path d="M4 12h16m-6-6 6 6-6 6"/></>,
  play: <path d="m8 5 11 7-11 7V5Z"/>,
  pause: <><path d="M8 5v14M16 5v14"/></>,
};

export default function PlacementIcon({ name, size = 22, className = "", label }) {
  return <svg className={`placement-icon ${className}`} width={size} height={size} viewBox="0 0 24 24"
    fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"
    role={label ? "img" : undefined} aria-label={label} aria-hidden={label ? undefined : true}>
    {paths[name] || paths.file}
  </svg>;
}
