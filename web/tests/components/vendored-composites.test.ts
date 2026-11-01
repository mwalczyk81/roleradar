import { describe, it, expect, vi } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";

import {
  DataTable,
  Toolbar,
  AppShell,
  EmptyState,
  Skeleton,
  Toast,
  ToastRegion,
  Menu,
  Tooltip,
} from "../../app/ui.js";

describe("Vendored Part C Design System Composites (T032)", () => {
  it("renders AppShell with full navigation, title, theme, userLabel, and signOut form", () => {
    const onToggle = vi.fn();
    const navItems = [
      { label: "Postings", href: "/postings", active: true },
      { label: "Companies", href: "/companies" },
      { label: "Applications", href: "/applications" },
    ];

    render(
      React.createElement(
        AppShell,
        {
          title: "RoleRadar Management",
          brand: "RoleRadar",
          userLabel: "local-owner",
          theme: "dark",
          onThemeToggle: onToggle,
          navItems,
          signOut: React.createElement(
            "form",
            { method: "post", action: "/auth/logout" },
            [
              React.createElement("input", {
                key: "csrf",
                type: "hidden",
                name: "_csrf",
                value: "mock-csrf",
              }),
              React.createElement(
                "button",
                { key: "btn", type: "submit" },
                "Sign Out"
              ),
            ]
          ),
        },
        React.createElement("div", { "data-testid": "main-content" }, "App Content")
      )
    );

    expect(screen.getByText("RoleRadar Management")).toBeDefined();
    expect(screen.getByText("local-owner")).toBeDefined();
    expect(screen.getByRole("button", { name: /Sign Out/i })).toBeDefined();
    expect(screen.getByTestId("main-content")).toBeDefined();
  });

  it("renders DataTable with data and columns", () => {
    const columns = [
      { key: "name", header: "Name" },
      { key: "val", header: "Value", numeric: true },
    ];
    const data = [{ id: "1", name: "Item One", val: 100 }];

    render(
      React.createElement(DataTable, {
        columns,
        data,
        keyField: "id",
        caption: "Test Items",
      })
    );

    expect(screen.getByText("Item One")).toBeDefined();
    expect(screen.getByText("100")).toBeDefined();
  });

  it("renders Toolbar with search and filter", () => {
    render(
      React.createElement(Toolbar, {
        searchPlaceholder: "Search here",
        filters: [{ id: "1", label: "Tag", value: "Active", onRemove: vi.fn() }],
      })
    );
    expect(screen.getByPlaceholderText("Search here")).toBeDefined();
    expect(screen.getByText(/Active/)).toBeDefined();
  });

  it("renders EmptyState and Skeleton", () => {
    const { container } = render(
      React.createElement("div", null, [
        React.createElement(EmptyState, {
          key: "empty",
          title: "No postings found",
          description: "Try adjusting your filters.",
        }),
        React.createElement(Skeleton, {
          key: "skel",
          width: "100%",
          height: "2rem",
        }),
      ])
    );
    expect(screen.getByText("No postings found")).toBeDefined();
    expect(screen.getByText("Try adjusting your filters.")).toBeDefined();
    expect(container.querySelector("[aria-hidden='true']")).toBeDefined();
  });

  it("renders Toast and ToastRegion", () => {
    render(
      React.createElement(
        ToastRegion,
        null,
        React.createElement(Toast, {
          title: "Update Saved",
          message: "Posting disposition changed to scored.",
          variant: "success",
        })
      )
    );
    expect(screen.getByText("Update Saved")).toBeDefined();
    expect(screen.getByText(/scored/)).toBeDefined();
  });

  it("renders Menu and Tooltip overlays", () => {
    render(
      React.createElement("div", null, [
        React.createElement(Menu, {
          key: "menu",
          trigger: "Actions",
          items: [{ label: "Edit" }, { label: "Delete", danger: true }],
        }),
        React.createElement(Tooltip, {
          key: "tt",
          content: "Information tip",
          children: React.createElement("span", null, "Hover target"),
        }),
      ])
    );
    expect(screen.getByText("Actions")).toBeDefined();
    expect(screen.getByText("Hover target")).toBeDefined();
  });
});
