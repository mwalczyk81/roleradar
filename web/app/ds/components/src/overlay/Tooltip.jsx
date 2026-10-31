import React from "react";

/**
 * Tooltip — a short text hint for one control. Wrap exactly one element (a button, a link, a
 * focusable badge): the tooltip shows on hover and on keyboard focus, hides on Escape, and
 * stays open while the pointer is over the hint itself. The element gets aria-describedby
 * pointing at the hint, so assistive tech reads it as the element's description. Keep the text
 * short and never put essential information only in a tooltip.
 */
export function Tooltip({ content, children, placement = "top", delay = 300, style = {}, ...rest }) {
  const [open, setOpen] = React.useState(false);
  const [shift, setShift] = React.useState(0);
  const bubble = React.useRef(null);
  const timer = React.useRef(null);
  const id = React.useId();
  const child = React.Children.only(children);

  const showSoon = () => {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setOpen(true), delay);
  };
  const showNow = () => {
    clearTimeout(timer.current);
    setOpen(true);
  };
  const hide = () => {
    clearTimeout(timer.current);
    setOpen(false);
  };

  React.useEffect(() => () => clearTimeout(timer.current), []);

  // Keep the hint inside the viewport: when it opens, slide it sideways if it would be clipped.
  React.useLayoutEffect(() => {
    if (!open || !bubble.current) return;
    const r = bubble.current.getBoundingClientRect();
    const vw = document.documentElement.clientWidth;
    const margin = 8;
    let next = shift;
    if (r.left < margin) next = shift + (margin - r.left);
    else if (vw && r.right > vw - margin) next = shift - (r.right - (vw - margin));
    if (next !== shift) setShift(next);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  React.useEffect(() => {
    if (!open) return;
    function onKey(e) {
      if (e.key === "Escape") hide();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);

  const described = [child.props["aria-describedby"], id].filter(Boolean).join(" ");
  const trigger = React.cloneElement(child, {
    "aria-describedby": described,
    onFocus: (e) => {
      child.props.onFocus && child.props.onFocus(e);
      showNow();
    },
    onBlur: (e) => {
      child.props.onBlur && child.props.onBlur(e);
      hide();
    },
  });

  const above = placement !== "bottom";
  return (
    <span {...rest} onMouseEnter={showSoon} onMouseLeave={hide} style={{ position: "relative", display: "inline-flex", ...style }}>
      {trigger}
      <span
        id={id}
        ref={bubble}
        role="tooltip"
        style={{
          position: "absolute",
          left: "50%",
          transform: `translateX(calc(-50% + ${shift}px))`,
          [above ? "bottom" : "top"]: "100%",
          [above ? "paddingBottom" : "paddingTop"]: "var(--ds-space-1)",
          zIndex: "var(--ds-z-overlay)",
          visibility: open ? "visible" : "hidden",
          opacity: open ? 1 : 0,
          transition: "opacity var(--ds-duration-fast) var(--ds-ease-out)",
        }}
      >
        <span
          style={{
            display: "block",
            width: "max-content",
            maxWidth: "16rem",
            padding: "var(--ds-space-1) var(--ds-space-2)",
            background: "var(--ds-text)",
            color: "var(--ds-bg)",
            borderRadius: "var(--ds-radius-md)",
            boxShadow: "var(--ds-shadow-md)",
            fontFamily: "var(--ds-font-sans)",
            fontSize: "var(--ds-text-xs)",
            fontWeight: "var(--ds-weight-medium)",
            lineHeight: "var(--ds-leading-normal)",
            textAlign: "left",
          }}
        >
          {content}
        </span>
      </span>
    </span>
  );
}
