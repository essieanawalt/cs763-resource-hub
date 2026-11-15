# cs763-resource-hub

Resource Allocation & Help Desk Hub (Project Portal, Domain 2), built with an AI developer agent (Claude Code) across the project phases. Each phase has its own folder with its source code, AI logs, documents and evidence.

| Folder | Phase | Contents |
|---|---|---|
| [`PA1/`](PA1/) | Service definition, tech stack and pipeline | My PA1 spec, agent environment, prompt test log, barebones app, AI logs, pipeline evidence. Release `pa1` |
| [`PA2/`](PA2/) | Naive baseline, threat modeling and SAST triage | The standardized Domain 2 spec, the test-first build in `functional-codebase/`, agent environment updates, AI logs, pipeline evidence |

Shared configuration stays at the root and is reused by each phase: `Dockerfile`, `docker-compose.yml`, `.github/workflows/devsecops.yml`, `ResourceHub.slnx`, `global.json`, `Directory.Build.props`. They currently build and scan `PA2/functional-codebase/`.

How to build, run and test the current service: [`PA2/README.md`](PA2/README.md).

## Running the PA2 release

- Check out the `pa2` tag. Ports 8080 and 5432 must be free on the host
- `docker compose up --build -d`, then `curl http://localhost:8080/api/v1/health` should return `200`. The example flow, tests and configuration are in [`PA2/README.md`](PA2/README.md)
- No setup is needed: the database password falls back to a local development value unless `POSTGRES_PASSWORD` is set
- PA2 documents: [`PA2/sast-triage-report.md`](PA2/sast-triage-report.md) (the confirmed CWEs are in the table at the top) and [`PA2/security-analysis.md`](PA2/security-analysis.md)

Stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core, Docker. Commits are labelled `AI:`, `HUMAN:` or `HYBRID:` by contribution.
