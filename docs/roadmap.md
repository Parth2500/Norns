# Roadmap

Written in October 2026, while the project has been on hold since January 2026. It describes the code as it stands and what to build next, in order.

## Done

- Solution structure: Domain, Application, Infrastructure, Api, Tests.
- Domain model for workflows, requests, identity and audit ([Domain model](domain-model.md)).
- EF Core configurations and the `InitialCreate` migration ([Schema](schema.md)).
- `POST /api/requests`: validates the input, rejects an unknown workflow with 404, saves the request.
- `GET /api/requests/{id}`: returns the request, or 404.
- Exception middleware: `ValidationException` → 400, `NotFoundException` → 404.
- Handler unit tests for both endpoints (5 tests, passing).
- Serilog request logging, Swagger in Development, Dockerfile for the API.
- **Auth with Heimdall** (October 2026). JWT bearer validation of Heimdall access tokens (RS256, `typ` = `at+jwt`, issuer, audience, user tokens only). Each Heimdall issuer maps to a CoreOps tenant in `Heimdall:Tenants`. `TenantId` and the user id come from the token and are no longer in request bodies. An EF Core global query filter limits every query to the token's tenant. Every endpoint requires a signed-in user.

## Direction (2026-11-14)

The owner decided how Norns fits the Asgard suite (decisions S10 and S12 in `Asgard/docs/decisions.md`). Nothing here is built. It changes what the steps under "Next" must allow for.

**One codebase, two places to install. Installations never sync.**

| | In a home | Online |
|---|---|---|
| Runs as | An extension of Bifröst: started by Bifröst, on a private socket, no network port | A hosted service, as today |
| Who is calling | Bifröst's headers: person, role, device | A Heimdall access token, as built in October 2026 |
| Tenant | One: the home | From the token's issuer, as today |
| Storage | SQLite | SQL Server or PostgreSQL |
| Reaches the admin | Through Bifröst's "ask the admin": the host screen, the admin's phone and a Huginn notice, at once | Its own front end |

**What that asks of the code**

1. **Storage that is not tied to SQL Server.** Keep EF Core provider-neutral, with SQLite as the home provider. SQL Server cannot be shipped to a home machine. Do this before the schema grows: `rowversion` (step 3 below) is SQL Server only, so use a concurrency token that every provider has.
2. **.NET 10,** to match Bifröst, Sleipnir and Saga.
3. **Sign-in behind one seam.** Today the tenant and user come from a Heimdall token. Put that behind an interface with two implementations: Heimdall tokens, and Bifröst's identity headers. The Application layer already takes the tenant and user as inputs, so the seam belongs in the Api project.
4. **A Bifröst extension manifest** and a start-up mode, as Sleipnir has (Bifröst design §8).
5. **No notifications of its own.** A transition that needs a person's answer goes through Bifröst's "ask the admin". Norns does not call Huginn.

**First home workflow:** "may this download start?" Sleipnir holds a download as pending and hands the request to Norns. Norns applies its rules (for example, approve automatically under a size) and its history, asks the admin when needed, and tells Sleipnir the result. Without Norns installed, Sleipnir asks Bifröst for a plain approve or reject, so Norns stays optional.

**Order.** Steps 1 to 4 under "Next" are still the core and come first. Items 1 and 3 above fit inside them. Items 2, 4 and 5 wait until Bifröst can host an extension.

## Next

1. **Workflow management.** Add methods to `WorkflowDefinition` for adding states and transitions, and set `TenantId` on each state and transition (the tenant query filter hides rows with `Guid.Empty`). Enforce the rules there: exactly one initial state, at least one terminal state, transitions only between states of the same workflow. Add `POST /api/workflows` (states and transitions in one body), `GET /api/workflows/{id}`, and activate/deactivate.
2. **Fix request creation.** Take the initial state from the workflow instead of the request body. Refuse inactive workflows. Write the first `RequestStateHistory` row (`FromStateCode = null`).
3. **Transitions.** Add `POST /api/requests/{id}/transitions` with `{ toStateCode, comment }`. Check that the transition exists, enforce `RequiresComment`, refuse requests already in a terminal state, and write history and audit rows in one transaction. Add a `rowversion` column on `Requests` first.
4. **List and history.** `GET /api/requests` filtered by state and paged, and `GET /api/requests/{id}/history`.
5. **Users from Heimdall.** Create or update the CoreOps `User` row on a user's first call, with the id from `sub` and the email from Heimdall's `/userinfo` (or an `email` claim once Heimdall adds one to access tokens).
6. **Authorization rules.** Decide who may make each transition, probably by role on `WorkflowTransition`.
7. **Front end** in `frontend/`. It signs in with Heimdall's authorization code flow with PKCE and asks for the CoreOps audience with the `resource` parameter.

## Known issues

| Issue | Where | Effect |
|---|---|---|
| No Heimdall client for CoreOps | Heimdall `seed-dev` | Heimdall's development seed registers no client with the CoreOps audience, so a local token for CoreOps needs a client registered by hand. |
| Users are not provisioned | `CoreOps.Domain/Identity/User` | A signed-in user has no `User` row until step 5. Nothing reads users yet. |
| Initial state comes from the caller and is not checked | `CreateRequestHandler` | A request can start in a state that does not exist. Fixed by step 2. |
| `TenantId` is never set on states and transitions | `WorkflowState`, `WorkflowTransition` | The column is always `Guid.Empty`, so the tenant query filter hides these rows. Fixed by step 1. See [Schema](schema.md#gaps-in-the-current-schema). |
| Missing foreign keys | Migration | Orphaned rows are possible. See [Schema](schema.md#gaps-in-the-current-schema). |
| Unhandled exceptions are not mapped | `ExceptionHandlingMiddleware` | Anything other than the two known exceptions falls through to the default 500 page. Consider `ProblemDetails` for every error. |
| SQL Server health check uses the old tools path | `docker/docker-compose.yml` | Current `mssql/server:2022-latest` images ship `sqlcmd` in `/opt/mssql-tools18/bin` and need `-C`, so the check may report the container unhealthy. |
| `sa` password is committed | `docker-compose.yml`, `appsettings.Development.json` | Fine for local development only. Move it to `.env` and user secrets, as Heimdall does. |
| No integration tests | `CoreOps.Tests` | Repositories, EF mappings, the tenant filter and token validation are untested. Testcontainers with SQL Server, and `WebApplicationFactory` with a test signing key, would cover them. |
| Serilog writes nowhere | `appsettings*.json` | The `Serilog` section has no `WriteTo`, so the API logs nothing, not even refused tokens. Add a console sink. |
