# CoreOps

CoreOps is a multi-tenant workflow engine for internal requests. A tenant defines workflows as states and allowed transitions (for example `draft → submitted → approved | rejected`). Users file requests against a workflow, and each request moves through it. Every move is kept in a state history, and every change can be written to an audit log.

**Status:** on hold since January 2026. The domain model, schema and first migration are done. Two endpoints work: create a request and read it by id. Sign-in uses [Heimdall](https://github.com/Parth2500/heimdall-idp): every endpoint needs a Heimdall access token, and the tenant and user come from that token. Moving a request between states, managing workflows, authorization rules and the front end are not built. See [Roadmap](docs/roadmap.md).

## Stack

- .NET 9, ASP.NET Core controllers, Swagger in Development
- SQL Server 2022 in Docker, EF Core 9
- Clean Architecture (dependencies point inward only), hand-written handlers (no MediatR)
- Serilog
- Authentication by [Heimdall](https://github.com/Parth2500/heimdall-idp) access tokens (JWT bearer)
- xUnit, Moq, FluentAssertions
- Multi-tenant from the first migration: every row carries a `TenantId`

## Repository layout

| Path | Content |
|---|---|
| `backend/CoreOps.Domain` | Entities. No dependencies. |
| `backend/CoreOps.Application` | Use cases (commands, queries, handlers, validators) and repository interfaces. |
| `backend/CoreOps.Infrastructure` | EF Core `DbContext`, entity configurations, migrations, repositories. |
| `backend/CoreOps.Api` | Controllers, request/response contracts, Heimdall authentication, exception middleware, composition root. |
| `backend/CoreOps.Tests` | Unit tests for the Application handlers. No database. |
| `docker/` | `docker-compose.yml` for SQL Server. |
| `frontend/` | Empty. Reserved for the UI. |
| `docs/` | Domain model, schema, roadmap. |

## Documents

- [Domain model](docs/domain-model.md): the entities, what each one means, and how a request moves through a workflow.
- [Schema](docs/schema.md): tables, schemas, keys and indexes as the `InitialCreate` migration builds them.
- [Roadmap](docs/roadmap.md): what is done, what comes next, and known issues.

## Build and test

You need a .NET SDK that can target .NET 9 (SDK 9 or 10). The tests need the .NET 9 runtime. With only the .NET 10 runtime installed, let them roll forward:

```
cd backend
dotnet build CoreOps.sln
DOTNET_ROLL_FORWARD=Major dotnet test CoreOps.sln
```

## Authentication

CoreOps does not sign anyone in itself. [Heimdall](https://github.com/Parth2500/heimdall-idp) does, and CoreOps accepts Heimdall access tokens in the `Authorization: Bearer` header. Each token must:

- be signed with RS256 by a key in the issuer's JWKS, with `typ` = `at+jwt` (an ID token is refused),
- come from an issuer listed in `Heimdall:Tenants`,
- have `aud` = `Heimdall:Audience`,
- belong to a user: `sub` must be a user id. A `client_credentials` token is refused.

Heimdall puts no tenant claim in its tokens. Each Heimdall tenant has its own issuer URL (`https://<tenant host>`), so `Heimdall:Tenants` maps each trusted issuer to a CoreOps `TenantId`:

```json
"Heimdall": {
  "Audience": "https://api.coreops.localhost",
  "Tenants": [
    { "Issuer": "https://acme.localhost:7200", "TenantId": "00000000-0000-0000-0000-0000000000a1" }
  ]
}
```

The user id is the token's `sub`. It is also the id of the CoreOps `User` row. Every query is filtered to the token's tenant by an EF Core global query filter. Every endpoint requires a signed-in user unless it opts out. The API refuses to start without `Heimdall:Audience` and at least one issuer.

To get tokens for CoreOps, register a client in Heimdall with `https://api.coreops.localhost` (or your audience) in its allowed audiences and the authorization code grant. Heimdall's `seed-dev` does not create one yet.

## Run locally

You need Docker (or Podman) for SQL Server, and a running Heimdall (see its README) to get tokens.

1. Start SQL Server:
   ```
   docker compose -f docker/docker-compose.yml up -d
   ```
   The `sa` password is `YourStrong!Passw0rd`. It is a development value, also in `appsettings.Development.json`. Change both for anything that is not your own machine.

2. Apply the migrations:
   ```
   cd backend
   dotnet tool install --global dotnet-ef   # once
   dotnet ef database update --project CoreOps.Infrastructure --startup-project CoreOps.Api
   ```

3. Let CoreOps trust Heimdall's development certificate. Heimdall serves `https://acme.localhost:7200` with the ASP.NET development certificate, which is issued for `localhost`. `Heimdall:DevelopmentTls` pins that certificate and checks it against the name `localhost`. It works only in Development. Export it once:
   ```
   cd CoreOps.Api
   mkdir -m 700 .local
   dotnet dev-certs https --export-path .local/heimdall-dev-ca.pem --format PEM --no-password
   rm .local/heimdall-dev-ca.key
   cd ..
   ```
   git ignores `.local/`.

4. Start the API:
   ```
   dotnet run --project CoreOps.Api --launch-profile http
   ```
   Swagger is at http://localhost:5201/swagger.

5. There is no endpoint to create workflows yet, so insert one by hand before creating a request:
   ```sql
   INSERT INTO workflow.WorkflowDefinitions (Id, TenantId, Name, IsActive, CreatedBy, CreatedAt)
   VALUES ('11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-0000000000a1',
           'Leave request', 1, '00000000-0000-0000-0000-0000000000b1', SYSUTCDATETIME());
   ```
   Then:
   ```
   curl -X POST http://localhost:5201/api/requests -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{
     "title": "Two days off in March",
     "description": null,
     "workflowDefinitionId": "11111111-1111-1111-1111-111111111111",
     "initialStateCode": "draft"
   }'
   curl http://localhost:5201/api/requests/<requestId> -H "Authorization: Bearer $TOKEN"
   ```
   `$TOKEN` is a Heimdall access token for the CoreOps audience, from the `acme.localhost` tenant.

## API

| Method | Path | Result |
|---|---|---|
| `POST` | `/api/requests` | `200` with `{ requestId }`. `400` with a list of errors if validation fails. `404` if the workflow definition does not exist in the caller's tenant. |
| `GET` | `/api/requests/{id}` | `200` with the request. `404` if it does not exist in the caller's tenant. |

Every endpoint returns `401` without a valid Heimdall access token. The tenant and the creator come from the token, never from the body.

Errors come from `ExceptionHandlingMiddleware` as `{ "message": ..., "errors": [...] }`. Any other exception falls through to ASP.NET Core's default handling.

## Related projects

[Heimdall](https://github.com/Parth2500/heimdall-idp) is the identity provider for CoreOps. See [Authentication](#authentication).
