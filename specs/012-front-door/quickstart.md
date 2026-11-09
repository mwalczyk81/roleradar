# Quickstart: validating Front Door

Run on a machine with Docker. Nothing here touches the real stack. Never run these against `roleradar-mongo` or port 27017. Command shapes are in [contracts/cli-demo.md](contracts/cli-demo.md); the dataset is in [data-model.md](data-model.md).

1. **Demo up** (first run builds four images; note the start and finish time for SC-001a): `docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml up --build --wait`. Expect every service healthy and `demo-seed` exited 0 with a counts line and no secret in its output.
2. **Credentials**: `docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml --profile tools run --rm demo-cli demo credentials`. Expect username, password and TOTP secret printed once. Run it with output redirected and expect a refusal.
3. **Sign in**: open `https://localhost:5103`, accept the certificate warning once for the web origin and once for `identity.localhost`, sign in with the printed values and a code from `... run --rm demo-cli demo totp` (or an authenticator app).
4. **Insights**: open `/insights`; funnels, tiles and both charts show data; click a count and confirm the list row count matches. Record the elapsed time since step 1.
5. **Real stack untouched**: `docker volume ls` still lists `roleradar-mongo-data`; no demo container is named `roleradar-mongo`.
6. **Guard**: `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~DemoModeGuard"`.
7. **No committed secret, no demo account in defaults**: `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~DemoAbsentFromDefaults|FullyQualifiedName~NoCommittedDemoSecret"`.
8. **Architecture rules**: `dotnet test tests/RoleRadar.Tests.Architecture -c Release --filter "FullyQualifiedName~DemoArchitecture"`.
9. **Screenshots** (with the credential variables set from step 2): `ROLERADAR_SCREENSHOT_DIR=docs/images ROLERADAR_DEMO_URL=https://localhost:5103 pnpm --filter web exec playwright test screenshots.spec.ts`; open all 14 images; replace the placeholder.
10. **Link, consistency and docs checks**: `pnpm --filter web run test -- front-door-links`.
11. **README consistency test**: `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~MongoOptionsReadmeConsistencyTests"`.
12. **Teardown**: `docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml down -v`.
13. **Owner-only, before announcing**: the commands in `docs/pre-announce.md`.
14. **Windows confirmation (owner)**: R-001, R-009, R-012 results noted in the PR description before the demo task is closed.
