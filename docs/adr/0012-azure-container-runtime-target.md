# ADR 0012: Azure Container Runtime Target — Container Apps vs. AKS

- **Status**: Proposed
- **Date**: 2026-10-18
- **Feature**: `003-linux-container-readiness`

## Context and Problem Statement

When RoleRadar introduces hosted HTTP services (Feature 005 API/web and Feature 006 hosted MCP over HTTP), the containerized workloads will be hosted on Azure.

As a single-owner, personal-first application, the compute platform must balance:

1. Operational simplicity and maintenance burden.
2. Cost efficiency (including scale-to-zero when idle).
3. Secret management and identity integration (Azure Key Vault, managed identities).
4. Support for background scheduled tasks (such as periodic database backups to Azure Blob Storage).

Which Azure compute platform should be selected: **Azure Container Apps (ACA)** or **Azure Kubernetes Service (AKS)**?

## Considered Options

1. **Azure Container Apps (ACA)**: Fully managed serverless container runtime built on top of Kubernetes (K8s/KEDA/Envoy) without exposing cluster infrastructure.
2. **Azure Kubernetes Service (AKS)**: Full enterprise Kubernetes cluster management.

## Comparison & Decision Drivers

| Factor | Azure Container Apps (ACA) | Azure Kubernetes Service (AKS) |
| :--- | :--- | :--- |
| **Operational Maintenance** | **Minimal**: No node pool upgrades, no Kubernetes version deprecations, no control plane management. | **High**: Regular Kubernetes version upgrades, node OS patch management, ingress controller updates. |
| **Scale to Zero & Cost** | **Native**: Scales HTTP workloads to 0 replicas when no traffic is received. Ideal for single-owner workloads; incurs negligible cost when idle. | **Limited/Costly**: Minimum node count (typically 1–3 system/worker nodes) incurs fixed monthly VM compute costs ($30–$100+/month) even when completely idle. |
| **Scheduled Jobs (Backups)** | **Built-in (ACA Jobs)**: Native cron/triggered job abstraction that spins up a container, executes `roleradar backup`, and tears down. | **CronJobs**: Requires standard Kubernetes CronJob manifests, RBAC, and node capacity. |
| **Networking & Ingress** | **Automatic**: Built-in Envoy ingress, automatic managed TLS certificates, path-based routing. | **Manual**: Requires configuring NGINX / Traefik / Application Gateway ingress, Cert-Manager, and Let's Encrypt. |
| **Secret & Identity Integration** | **Native**: Direct integration with Azure Key Vault secrets and User-Assigned Managed Identities. | **Add-on**: Requires Azure Key Vault Secrets Store CSI Driver. |
| **Developer Expertise** | Matt knows AKS deeply, but RoleRadar is an unmanaged personal project where administrative overhead should be minimized. | Familiar enterprise surface, but excess complexity for a single-user system. |

## Decision Outcome

Recommended target: **Azure Container Apps (ACA)**.

### Rationale

- **Cost & Scale-to-Zero**: RoleRadar is used by a single person and personal AI agents. ACA's ability to scale HTTP and MCP services to zero instances during idle periods provides significant cost savings compared to running a persistent AKS cluster.
- **Dedicated Scheduled Jobs**: Periodic database backups can be executed via an **Azure Container Apps Job** on a daily cron schedule, executing the hardened `roleradar-cli` container to write encrypted archives directly to Azure Blob Storage.
- **Reduced Cognitive Overhead**: ACA abstracts away Kubernetes cluster maintenance, API version migrations, and ingress controllers while still maintaining standard OCI container portability. If multi-tenant enterprise scaling is ever needed in the future, the identical container images can be moved directly to AKS.

> [!NOTE]
> This decision documents the target architecture for future HTTP milestones (005/006). Neither Azure Container Apps nor AKS resources are provisioned in Feature 003.

### Open Items

Verification of cold-start latency under real ACA consumption plan environments and Azure Blob backup job execution to be completed after running the external-mode suite against a real candidate, with the checklist filled in as far as can be done locally.
