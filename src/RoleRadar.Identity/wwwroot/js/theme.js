// Sets the kit theme from the system colour scheme and follows live changes (009 US4, ADR 0034).
// Blocking on purpose: it is the first element in <head>, so the theme is set before the first paint.
// No cookie, no storage, no inline use: the pages load it with a plain src, which the CSP allows (script-src 'self').
(function () {
  var query = window.matchMedia("(prefers-color-scheme: dark)");

  function apply() {
    document.documentElement.setAttribute("data-theme", query.matches ? "dark" : "light");
  }

  apply();
  query.addEventListener("change", apply);
})();
