/**
 * The app name as the shell brand: "RoleRadar" in the display face, with a small radar mark in the accent colour.
 * The mark is decorative (aria-hidden), so the accessible name stays "RoleRadar". Styled by classes in app.css
 * (the CSP forbids inline style attributes).
 */
export function Brand() {
  return (
    <span className="brand">
      <svg
        className="brand-mark"
        viewBox="0 0 24 24"
        width="22"
        height="22"
        aria-hidden="true"
        focusable="false"
      >
        <circle cx="12" cy="12" r="9.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
        <circle cx="12" cy="12" r="5" fill="none" stroke="currentColor" strokeWidth="1.5" />
        <line x1="12" y1="12" x2="18.5" y2="5.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
        <circle cx="18.5" cy="5.5" r="1.6" fill="currentColor" />
      </svg>
      <span className="brand-name">RoleRadar</span>
    </span>
  );
}
