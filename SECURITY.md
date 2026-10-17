# Security Policy

## Supported Versions

RoleRadar is developed on the `main` branch targeting `.NET 10`. Security fixes are applied to `main`.

| Version | Supported          |
| ------- | ------------------ |
| `0.1.x` | :white_check_mark: |

## Reporting a Vulnerability

Please report suspected security vulnerabilities via **GitHub Private Vulnerability Reporting** (`Security` -> `Report a vulnerability`) rather than opening a public issue.

### Scope & Privacy Guardrails

- **Local-First Architecture**: RoleRadar runs exclusively over `stdio` for MCP communication and binds its local MongoDB replica set strictly to `127.0.0.1:27017`.
- **Zero Personal Data in Repository**: Per Constitution Principle I, real job postings, company opinions, salary figures, or personal contact data must never be committed to this repository. If an accidental data leak is discovered in commit history, report it immediately for history scrubbing and rotation.
