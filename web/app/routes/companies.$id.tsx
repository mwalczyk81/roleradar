import * as React from "react";
import { Brand } from "../components/Brand.js";
import type { LoaderFunctionArgs, ActionFunctionArgs } from "react-router";
import { useLoaderData, useFetcher, useRevalidator, Link, data } from "react-router";
import { ArrowLeft, Plus, Trash2 } from "lucide-react";
import { requireUserSession, assertApiOk } from "../lib/auth-guard.server.js";
import { validateCsrfToken } from "../lib/csrf.server.js";
import { createApiClient } from "../lib/api-client.server.js";
import { useTheme } from "../lib/use-theme.js";
import { getNavItems } from "../lib/nav.js";
import {
  COMPANY_STATUS_OPTIONS,
  OWNERSHIP_TYPE_OPTIONS,
  COMPANY_STATUS_METADATA,
  FETCH_METHOD_OPTIONS,
  isCompanyStatus,
  isFetchMethod,
  isOwnershipType,
  type FetchMethod,
} from "../lib/company-enums.js";
import {
  AppShell,
  Card,
  Input,
  Select,
  Textarea,
  Button,
  Badge,
  Alert,
  Toast,
  ToastRegion,
} from "../ui.js";
import type { components } from "../../../contracts/client/api-types.js";

type CompanyDetailDto = components["schemas"]["CompanyDetailDto"];
type CareerSourceDto = components["schemas"]["CareerSourceDto"];
type UpdateCareerSourceRequestDto = components["schemas"]["UpdateCareerSourceRequestDto"];
type UpdateCompanyRequestDto = components["schemas"]["UpdateCompanyRequestDto"];

export interface CompanyActionData {
  ok: boolean;
  company?: CompanyDetailDto;
  conflict?: boolean;
  error?: string;
  fieldErrors?: Record<string, string>;
}

export async function loader({ request, params }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const companyId = params.id;

  if (!companyId) {
    throw new Response("Missing company ID", { status: 400 });
  }

  const client = createApiClient(session.accessToken);
  const { data: company, response, error } = await client.GET("/api/v1/companies/{id}", {
    params: {
      path: { id: companyId },
    },
  });

  await assertApiOk(response, request, session.sessionId, { notFoundMessage: "Company not found" });

  if (error || !company) {
    throw new Response(error?.message || "Failed to load company", {
      status: response.status || 500,
    });
  }

  return {
    company,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

export async function action({ request, params }: ActionFunctionArgs) {
  const session = await requireUserSession(request);
  const companyId = params.id;

  if (!companyId) {
    return data<CompanyActionData>(
      { ok: false, error: "Missing company ID" },
      { status: 400 }
    );
  }

  const formData = await request.formData();
  const csrfToken = formData.get("_csrf");

  if (!validateCsrfToken(csrfToken?.toString(), session.csrfToken)) {
    return data<CompanyActionData>(
      { ok: false, error: "Invalid CSRF token: operation not authorized." },
      { status: 403 }
    );
  }

  const name = formData.get("name")?.toString()?.trim() || "";
  const statusRaw = formData.get("status")?.toString()?.trim() || null;
  const ownershipTypeRaw = formData.get("ownershipType")?.toString()?.trim() || null;
  // Values come from the generated API unions; anything else is answered here like the API would (400 field error).
  const status = statusRaw === null ? null : isCompanyStatus(statusRaw) ? statusRaw : undefined;
  const ownershipType = ownershipTypeRaw === null ? null : isOwnershipType(ownershipTypeRaw) ? ownershipTypeRaw : undefined;
  if (status === undefined || ownershipType === undefined) {
    const enumErrors: Record<string, string> = {};
    if (status === undefined) enumErrors.status = "Status is not recognised.";
    if (ownershipType === undefined) enumErrors.ownershipType = "Ownership type is not recognised.";
    return data<CompanyActionData>(
      { ok: false, error: "Validation failed.", fieldErrors: enumErrors },
      { status: 400 }
    );
  }
  const description = formData.get("description")?.toString()?.trim() || null;
  const headquarters = formData.get("headquarters")?.toString()?.trim() || null;
  const versionToken = formData.get("versionToken")?.toString()?.trim() || null;

  let categoryTags: string[] | null = null;
  const rawTags = formData.get("categoryTags");
  if (typeof rawTags === "string" && rawTags.trim()) {
    try {
      const parsed = JSON.parse(rawTags);
      if (Array.isArray(parsed)) {
        categoryTags = parsed.filter((t): t is string => typeof t === "string");
      }
    } catch {
      categoryTags = null;
    }
  }

  let careerSources: UpdateCareerSourceRequestDto[] | null = null;
  const rawSources = formData.get("careerSources");
  if (typeof rawSources === "string" && rawSources.trim()) {
    try {
      const parsed = JSON.parse(rawSources);
      if (Array.isArray(parsed)) {
        careerSources = parsed as UpdateCareerSourceRequestDto[];
      }
    } catch {
      careerSources = null;
    }
  }

  const updateBody: UpdateCompanyRequestDto = {
    name,
    status,
    ownershipType,
    description,
    headquarters,
    categoryTags,
    careerSources,
    versionToken,
  };

  const client = createApiClient(session.accessToken);
  const { data: updatedCompany, response, error } = await client.PATCH("/api/v1/companies/{id}", {
    params: {
      path: { id: companyId },
    },
    body: updateBody,
  });

  await assertApiOk(response, request, session.sessionId);

  if (response.status === 409) {
    return data<CompanyActionData>(
      {
        ok: false,
        conflict: true,
        error: error?.message || "This company was modified by another operation. Please reload to see latest data.",
      },
      { status: 409 }
    );
  }

  if (response.status === 400 || error) {
    const fieldErrors: Record<string, string> = {};
    if (error && "details" in error && Array.isArray(error.details)) {
      for (const item of error.details) {
        if (item && item.field) {
          fieldErrors[item.field] = item.issue;
        }
      }
    }
    return data<CompanyActionData>(
      {
        ok: false,
        error: error?.message || "Validation failed.",
        fieldErrors,
      },
      { status: 400 }
    );
  }

  return data<CompanyActionData>(
    {
      ok: true,
      company: updatedCompany,
    },
    { status: 200 }
  );
}

function isValidHttpUrl(urlString: string): boolean {
  try {
    const u = new URL(urlString);
    return u.protocol === "http:" || u.protocol === "https:";
  } catch {
    return false;
  }
}

export default function CompanyDetail() {
  const loaderData = useLoaderData<typeof loader>();
  const fetcher = useFetcher<CompanyActionData>();
  const revalidator = useRevalidator();
  const { theme, toggleTheme } = useTheme();

  const [conflictToastDismissed, setConflictToastDismissed] = React.useState(false);
  const [successToastDismissed, setSuccessToastDismissed] = React.useState(false);

  // Form local state initialized from current company data
  const company = fetcher.data?.ok && fetcher.data.company ? fetcher.data.company : loaderData.company;

  const [name, setName] = React.useState(company.name);
  const [status, setStatus] = React.useState(company.status);
  const [ownershipType, setOwnershipType] = React.useState(company.ownershipType);
  const [description, setDescription] = React.useState(company.description || "");
  const [headquarters, setHeadquarters] = React.useState(company.headquarters || "");
  const [categoryTags, setCategoryTags] = React.useState<string[]>(company.categoryTags || []);
  const [newTagInput, setNewTagInput] = React.useState("");

  const [careerSources, setCareerSources] = React.useState<UpdateCareerSourceRequestDto[]>(
    (company.careerSources || []).map((cs: CareerSourceDto) => ({
      sourceType: cs.sourceType,
      atsProvider: cs.atsProvider,
      atsTenant: cs.atsTenant,
      sourceUrl: cs.sourceUrl,
      fetchMethod: cs.fetchMethod,
      active: cs.active,
    }))
  );

  // New career source input state
  const [newSourceUrl, setNewSourceUrl] = React.useState("");
  const [newAtsProvider, setNewAtsProvider] = React.useState("custom");
  const [newAtsTenant, setNewAtsTenant] = React.useState("");
  const [newFetchMethod, setNewFetchMethod] = React.useState<FetchMethod>("api");

  // Keep form in sync when company is reloaded or refreshed
  React.useEffect(() => {
    setName(company.name);
    setStatus(company.status);
    setOwnershipType(company.ownershipType);
    setDescription(company.description || "");
    setHeadquarters(company.headquarters || "");
    setCategoryTags(company.categoryTags || []);
    setCareerSources(
      (company.careerSources || []).map((cs: CareerSourceDto) => ({
        sourceType: cs.sourceType,
        atsProvider: cs.atsProvider,
        atsTenant: cs.atsTenant,
        sourceUrl: cs.sourceUrl,
        fetchMethod: cs.fetchMethod,
        active: cs.active,
      }))
    );
  }, [company]);

  const navItems = getNavItems("/companies");

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={loaderData.csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const isConflict = Boolean(
    fetcher.data?.ok === false && fetcher.data?.conflict && !conflictToastDismissed
  );
  const isSuccess = Boolean(
    fetcher.data?.ok === true && !successToastDismissed
  );

  const fieldErrors = fetcher.data?.fieldErrors || {};

  const handleAddTag = () => {
    const trimmed = newTagInput.trim();
    if (trimmed && !categoryTags.includes(trimmed)) {
      setCategoryTags([...categoryTags, trimmed]);
      setNewTagInput("");
    }
  };

  const handleRemoveTag = (tagToRemove: string) => {
    setCategoryTags(categoryTags.filter((t) => t !== tagToRemove));
  };

  const handleAddCareerSource = () => {
    const trimmedUrl = newSourceUrl.trim();
    if (!trimmedUrl) return;

    const newSource: UpdateCareerSourceRequestDto = {
      sourceType: "ats",
      atsProvider: newAtsProvider.trim() || "custom",
      atsTenant: newAtsTenant.trim() || null,
      sourceUrl: trimmedUrl,
      fetchMethod: newFetchMethod,
      active: true,
    };

    setCareerSources([...careerSources, newSource]);
    setNewSourceUrl("");
    setNewAtsTenant("");
  };

  const handleRemoveCareerSource = (indexToRemove: number) => {
    setCareerSources(careerSources.filter((_, idx) => idx !== indexToRemove));
  };

  const statusMeta = isCompanyStatus(status)
    ? COMPANY_STATUS_METADATA[status]
    : {
        label: String(status ?? "—"),
        tone: "neutral" as const,
      };

  return (
    <AppShell
      brand={<Brand />}
      title={company.name}
      userLabel={loaderData.userLabel}
      signOut={signOutForm}
      navItems={navItems}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div className="page-stack">
        {/* Breadcrumb Navigation */}
        <div>
          <Link
            to="/companies"
            className="back-link"
          >
            <ArrowLeft size={16} />
            <span>Back to Companies</span>
          </Link>
        </div>

        {/* Company Header Card. */}
        <Card padding="md">
          <div
            className="detail-header-row"
          >
            <div>
              <h2 className="page-heading detail-heading-xl">
                {company.name}
              </h2>
              <div
                className="detail-badges"
              >
                <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                {company.headquarters ? (
                  <span className="field-caption">
                    {company.headquarters}
                  </span>
                ) : null}
              </div>
            </div>
          </div>
        </Card>

        {/* General Action Error Banner */}
        {fetcher.data?.ok === false && fetcher.data?.error && !fetcher.data?.conflict ? (
          <Alert variant="danger" title="Error Saving Changes">
            {fetcher.data.error}
          </Alert>
        ) : null}

        {/* Edit Company Form */}
        <Card>
          <fetcher.Form
            method="post"
            className="form-stack"
          >
            <input type="hidden" name="_csrf" value={loaderData.csrfToken} />
            <input type="hidden" name="versionToken" value={company.versionToken} />
            <input type="hidden" name="categoryTags" value={JSON.stringify(categoryTags)} />
            <input type="hidden" name="careerSources" value={JSON.stringify(careerSources)} />

            <h2 className="page-heading form-heading">
              Edit Company Profile
            </h2>

            {/* Name Field */}
            <div>
              <label
                htmlFor="company-name-input"
                className="form-label"
              >
                Company Name *
              </label>
              <Input
                id="company-name-input"
                name="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                aria-invalid={Boolean(fieldErrors.name)}
                aria-describedby={fieldErrors.name ? "company-name-error" : undefined}
              />
              {fieldErrors.name ? (
                <div
                  id="company-name-error"
                  className="form-error"
                >
                  {fieldErrors.name}
                </div>
              ) : null}
            </div>

            {/* Status and Ownership Type Grid */}
            <div
              className="form-grid"
            >
              <div>
                <label
                  htmlFor="company-status-select"
                  className="form-label"
                >
                  Status *
                </label>
                <Select
                  id="company-status-select"
                  name="status"
                  value={status}
                  onChange={(e) => {
                    if (isCompanyStatus(e.target.value)) setStatus(e.target.value);
                  }}
                  aria-invalid={Boolean(fieldErrors.status)}
                  aria-describedby={fieldErrors.status ? "company-status-error" : undefined}
                >
                  {COMPANY_STATUS_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </Select>
                {fieldErrors.status ? (
                  <div
                    id="company-status-error"
                    className="form-error"
                  >
                    {fieldErrors.status}
                  </div>
                ) : null}
              </div>

              <div>
                <label
                  htmlFor="company-ownership-select"
                  className="form-label"
                >
                  Ownership Type
                </label>
                <Select
                  id="company-ownership-select"
                  name="ownershipType"
                  value={ownershipType}
                  onChange={(e) => {
                    if (isOwnershipType(e.target.value)) setOwnershipType(e.target.value);
                  }}
                  aria-invalid={Boolean(fieldErrors.ownershipType)}
                  aria-describedby={fieldErrors.ownershipType ? "company-ownership-error" : undefined}
                >
                  {OWNERSHIP_TYPE_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </Select>
                {fieldErrors.ownershipType ? (
                  <div
                    id="company-ownership-error"
                    className="form-error"
                  >
                    {fieldErrors.ownershipType}
                  </div>
                ) : null}
              </div>
            </div>

            {/* Headquarters Field */}
            <div>
              <label
                htmlFor="company-hq-input"
                className="form-label"
              >
                Headquarters
              </label>
              <Input
                id="company-hq-input"
                name="headquarters"
                value={headquarters}
                onChange={(e) => setHeadquarters(e.target.value)}
                placeholder="e.g. San Francisco, CA"
              />
            </div>

            {/* Description Field */}
            <div>
              <label
                htmlFor="company-desc-input"
                className="form-label"
              >
                Description
              </label>
              <Textarea
                id="company-desc-input"
                name="description"
                rows={3}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Overview of company, tech focus, domain, etc."
              />
            </div>

            {/* Category Tags List Editor */}
            <div>
              <label
                htmlFor="category-tag-input"
                className="form-label"
              >
                Category Tags
              </label>
              <div
                className="chip-row"
              >
                {categoryTags.map((tag) => (
                  <span
                    key={tag}
                    className="chip"
                  >
                    <span>{tag}</span>
                    <button
                      type="button"
                      aria-label={`Remove category tag ${tag}`}
                      onClick={() => handleRemoveTag(tag)}
                      className="chip-remove"
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
              <div className="inline-form-row">
                <Input
                  id="category-tag-input"
                  placeholder="New tag..."
                  value={newTagInput}
                  onChange={(e) => setNewTagInput(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      handleAddTag();
                    }
                  }}
                />
                <Button type="button" variant="secondary" onClick={handleAddTag}>
                  <Plus size={16} />
                  <span>Add Tag</span>
                </Button>
              </div>
            </div>

            {/* Career Sources List Editor */}
            <div>
              <div
                className="form-subheading"
              >
                Career Sources
              </div>

              {careerSources.length === 0 ? (
                <div
                  className="empty-caption"
                >
                  No career sources registered.
                </div>
              ) : (
                <div
                  className="source-list"
                >
                  {careerSources.map((source, idx) => {
                    const isHttp = isValidHttpUrl(source.sourceUrl || "");
                    return (
                      <div
                        key={idx}
                        className="source-row"
                      >
                        <div className="source-details">
                          <div className="source-url">
                            {isHttp ? (
                              <a
                                href={source.sourceUrl || "#"}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="underline-link"
                              >
                                {source.sourceUrl}
                              </a>
                            ) : (
                              <span>{source.sourceUrl}</span>
                            )}
                          </div>
                          <div
                            className="source-meta"
                          >
                            <span>Provider: {source.atsProvider || "custom"}</span>
                            {source.atsTenant ? <span>Tenant: {source.atsTenant}</span> : null}
                            <span>Method: {source.fetchMethod}</span>
                          </div>
                        </div>

                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          aria-label={`Remove career source ${source.sourceUrl}`}
                          onClick={() => handleRemoveCareerSource(idx)}
                        >
                          <Trash2 size={16} />
                        </Button>
                      </div>
                    );
                  })}
                </div>
              )}

              {/* Add Career Source subform */}
              <div
                className="subform-panel"
              >
                <div className="subform-title">
                  Add Career Source
                </div>
                <div
                  className="subform-grid"
                >
                  <div>
                    <label
                      htmlFor="new-source-url"
                      className="form-label-small"
                    >
                      Career Page / ATS URL *
                    </label>
                    <Input
                      id="new-source-url"
                      placeholder="https://jobs.example.com"
                      value={newSourceUrl}
                      onChange={(e) => setNewSourceUrl(e.target.value)}
                    />
                  </div>

                  <div>
                    <label
                      htmlFor="new-ats-provider"
                      className="form-label-small"
                    >
                      ATS Provider
                    </label>
                    <Input
                      id="new-ats-provider"
                      placeholder="e.g. greenhouse, lever, custom"
                      value={newAtsProvider}
                      onChange={(e) => setNewAtsProvider(e.target.value)}
                    />
                  </div>

                  <div>
                    <label
                      htmlFor="new-ats-tenant"
                      className="form-label-small"
                    >
                      Tenant / Org Slug
                    </label>
                    <Input
                      id="new-ats-tenant"
                      placeholder="Optional tenant slug"
                      value={newAtsTenant}
                      onChange={(e) => setNewAtsTenant(e.target.value)}
                    />
                  </div>

                  <div>
                    <label
                      htmlFor="new-fetch-method"
                      className="form-label-small"
                    >
                      Fetch Method
                    </label>
                    <Select
                      id="new-fetch-method"
                      value={newFetchMethod}
                      onChange={(e) => {
                        if (isFetchMethod(e.target.value)) setNewFetchMethod(e.target.value);
                      }}
                    >
                      {FETCH_METHOD_OPTIONS.map((opt) => (
                        <option key={opt.value} value={opt.value}>
                          {opt.label}
                        </option>
                      ))}
                    </Select>
                  </div>
                </div>

                <div>
                  <Button type="button" variant="secondary" onClick={handleAddCareerSource}>
                    <Plus size={16} />
                    <span>Add Source</span>
                  </Button>
                </div>
              </div>
            </div>

            {/* Submit Action Controls */}
            <div className="form-actions">
              <Button
                type="submit"
                variant="primary"
                disabled={fetcher.state !== "idle"}
              >
                {fetcher.state !== "idle" ? "Saving..." : "Save Changes"}
              </Button>
            </div>
          </fetcher.Form>
        </Card>

        {/* 409 Concurrency Conflict Toast Notification */}
        {isConflict ? (
          <ToastRegion>
            <Toast
              title="Concurrency Conflict"
              variant="warning"
              message={
                fetcher.data?.error ||
                "This company was modified by another operation. Please reload to see the latest changes."
              }
              action={{
                label: "Reload",
                onClick: () => {
                  setConflictToastDismissed(true);
                  revalidator.revalidate();
                },
              }}
              onDismiss={() => setConflictToastDismissed(true)}
            />
          </ToastRegion>
        ) : null}

        {/* Success Toast Notification */}
        {isSuccess ? (
          <ToastRegion>
            <Toast
              title="Changes Saved"
              variant="success"
              message="Company profile has been updated successfully."
              onDismiss={() => setSuccessToastDismissed(true)}
            />
          </ToastRegion>
        ) : null}
      </div>
    </AppShell>
  );
}
