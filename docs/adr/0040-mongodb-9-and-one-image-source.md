# ADR 0040: MongoDB 9.0, With One Source for the Image Tag

- **Status**: Accepted
- **Date**: 2026-11-09
- **Feature**: `chore/mongo-9-upgrade`

## Context and Problem Statement

Dependabot's new `docker-compose` ecosystem proposed `mongo:8.0.34` to `mongo:9.0.4` in the real and demo compose files (PR 69) and it merged, while 15 test files, the real-stack end-to-end test and the isolated container fixture each hard-coded `mongo:8.0.34`. `main` therefore ran two server versions at once, and a Dependabot bump of the compose files could never move the tests.

## Decision Outcome

- **Run on MongoDB 9.0.** The real stack, the demo and every test container use `mongo:9.0.4`.
- **One source.** `deploy/docker-compose.yml` is the only place that names the tag for tests. `MongoTestImage.Tag` (linked into the Contract and Integration projects) reads the first `image: mongo:` line, and the two raw `docker run` fixtures use it too. A Dependabot bump now moves compose and tests together.
- **Database Tools 100.20.1.** The pinned backup tools in the root Dockerfile move from 100.19.1 to 100.20.1, the version whose documentation lists MongoDB 9.0 as supported. The SHA-256 pins for both architectures were checked against the published `release.json` feed.
- **Hold the feature compatibility version at 8.0 on the real database.** The 9.0 binary runs on a database at compatibility version 8.0, and rolling back to 8.0.x stays possible until the version is raised. Raise it to 9.0 only after a burn-in period of normal use, and take an encrypted backup before the upgrade and before raising it.
- **Evidence, not documentation, for the driver.** The C# driver's compatibility table for server 9.0 could not be read when this was decided, so the full Contract and Integration suites running on 9.0.4 (including backup and restore through the pinned tools) are the compatibility check.

## Consequences

- **Good**: no second copy of the version; one place to change; the demo, tests and real stack agree.
- **Bad**: a Mongo major is now followed rather than held back; the real data volume needs the backup and burn-in discipline above. The `GLIBC_TUNABLES` workaround (SERVER-121912) stays in the fixtures until the upstream fix is confirmed for 9.0.
