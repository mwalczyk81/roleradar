import { describe, it, expect } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import AuthLogout, { loader } from "../../app/routes/auth.logout.js";

describe("auth.logout route", () => {
  it("loader returns a 200 OK response", async () => {
    const response = await loader();
    expect(response.status).toBe(200);
  });

  it("renders Signed Out page with heading, message, and Sign in link to /postings", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/auth/logout",
        Component: AuthLogout,
      },
    ]);

    render(<AppStub initialEntries={["/auth/logout"]} />);

    expect(await screen.findByRole("heading", { name: "Signed Out" })).toBeDefined();
    expect(screen.getByText("You have been signed out of RoleRadar.")).toBeDefined();

    const signInLink = screen.getByRole("link", { name: "Sign in" });
    expect(signInLink).toBeDefined();
    expect(signInLink.getAttribute("href")).toBe("/postings");
  });
});
