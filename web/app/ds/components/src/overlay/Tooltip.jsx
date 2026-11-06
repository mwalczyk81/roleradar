import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Tooltip — a short text hint for one control. Wrap exactly one element (a button, a link, a
 * focusable badge): the tooltip shows on hover and on keyboard focus, hides on Escape, and
 * stays open while the pointer is over the hint itself. The element gets aria-describedby
 * pointing at the hint, so assistive tech reads it as the element's description. Keep the text
 * short and never put essential information only in a tooltip.
 */
export function Tooltip({ content, children, placement = "top", delay = 300, className, style, ...rest }) {
  const [open, setOpen] = React.useState(false);
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
  // The offset is set through the CSSOM (a style attribute would be blocked by the page's policy).
  React.useLayoutEffect(() => {
    const el = bubble.current;
    if (!el) return;
    if (!open) {
      el.style.removeProperty("--ds-tip-shift");
      return;
    }
    el.style.setProperty("--ds-tip-shift", "0px");
    const r = el.getBoundingClientRect();
    const vw = document.documentElement.clientWidth;
    const margin = 8;
    let shift = 0;
    if (r.left < margin) shift = margin - r.left;
    else if (vw && r.right > vw - margin) shift = -(r.right - (vw - margin));
    el.style.setProperty("--ds-tip-shift", `${shift}px`);
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
    <span {...rest} className={cx("ds-tooltip", className)} onMouseEnter={showSoon} onMouseLeave={hide} style={style}>
      {trigger}
      <span id={id} ref={bubble} role="tooltip" className="ds-tooltip-pos" data-placement={above ? "top" : "bottom"} data-open={open ? "" : undefined}>
        <span className="ds-tooltip-bubble">{content}</span>
      </span>
    </span>
  );
}
