import { describe, it, expect } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import CriteriaPage from "../../app/routes/criteria.js";
import NotesPage from "../../app/routes/notes._index.js";
import NoteDetailPage from "../../app/routes/notes.$id.js";
import SearchRunsPage from "../../app/routes/search-runs.js";

describe("Operational Views Accessibility (T055: Light & Dark Axe)", () => {
  const sampleCriterion = {
    id: "crit-1",
    name: "Remote Only",
    weight: "hard_skip",
    ruleText: "Must be 100% remote within US.",
    effectiveFrom: "2026-01-01T00:00:00Z",
    effectiveTo: null,
    isActive: true,
  };

  const sampleNote = {
    id: "note-1",
    heading: "Senior Backend Review Notes",
    content: "Detailed technical evaluation and assessment notes.\nPreserving line breaks properly.",
    tags: ["backend", "interview"],
    linkedEntityType: "posting",
    linkedEntityId: "post-100",
    createdAt: "2026-10-15T12:00:00Z",
    updatedAt: "2026-10-16T12:00:00Z",
  };

  const sampleRun = {
    id: "run-1",
    runDate: "2026-10-31T08:00:00Z",
    newPostings: 3,
    summary: "Automated batch search run completed with 3 new postings.",
  };

  const CriteriaStub = createRoutesStub([
    {
      path: "/criteria",
      Component: CriteriaPage,
      loader: () => ({
        items: [sampleCriterion],
        csrfToken: "mock-csrf",
        userLabel: "operational-user",
      }),
    },
  ]);

  const NotesListStub = createRoutesStub([
    {
      path: "/notes",
      Component: NotesPage,
      loader: () => ({
        items: [sampleNote],
        totalCount: 1,
        limit: 25,
        offset: 0,
        q: "backend",
        csrfToken: "mock-csrf",
        userLabel: "operational-user",
      }),
    },
  ]);

  const NoteDetailStub = createRoutesStub([
    {
      path: "/notes/:id",
      Component: NoteDetailPage,
      loader: () => ({
        note: sampleNote,
        csrfToken: "mock-csrf",
        userLabel: "operational-user",
      }),
    },
  ]);

  const SearchRunsStub = createRoutesStub([
    {
      path: "/search-runs",
      Component: SearchRunsPage,
      loader: () => ({
        items: [sampleRun],
        totalCount: 1,
        limit: 25,
        offset: 0,
        csrfToken: "mock-csrf",
        userLabel: "operational-user",
      }),
    },
  ]);

  it("has zero serious or critical a11y violations on /criteria in light and dark", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(CriteriaStub, { key: "crit-light", initialEntries: ["/criteria"] }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(CriteriaStub, { key: "crit-dark", initialEntries: ["/criteria"] }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });

  it("has zero serious or critical a11y violations on /notes list in light and dark", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(NotesListStub, { key: "notes-light", initialEntries: ["/notes"] }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(NotesListStub, { key: "notes-dark", initialEntries: ["/notes"] }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });

  it("has zero serious or critical a11y violations on /notes/:id detail in light and dark", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(NoteDetailStub, { key: "note-detail-light", initialEntries: ["/notes/note-1"] }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(NoteDetailStub, { key: "note-detail-dark", initialEntries: ["/notes/note-1"] }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });

  it("has zero serious or critical a11y violations on /search-runs in light and dark", async () => {
    const { container: light } = render(
      React.createElement("div", { "data-theme": "light" }, [
        React.createElement(SearchRunsStub, { key: "runs-light", initialEntries: ["/search-runs"] }),
      ])
    );
    await expect(light).toHaveNoViolations();

    const { container: dark } = render(
      React.createElement("div", { "data-theme": "dark" }, [
        React.createElement(SearchRunsStub, { key: "runs-dark", initialEntries: ["/search-runs"] }),
      ])
    );
    await expect(dark).toHaveNoViolations();
  });
});
