# Domain model

All entities live in `backend/CoreOps.Domain`. Every entity except `UserRole` derives from `Entity`, which provides `Id` (a `Guid`) and `TenantId`. Properties have private setters. Entities are created only through their constructors, and each one has a private parameterless constructor for EF Core.

The model has four areas, and each maps to its own SQL schema (see [Schema](schema.md)).

## Workflow

A workflow is a small state machine that a tenant defines.

| Entity | Meaning |
|---|---|
| `WorkflowDefinition` | A named workflow, such as "Leave request". Owns its states and transitions. Created inactive (`IsActive = false`). |
| `WorkflowState` | One state in a workflow. `Code` is the stable key (`draft`, `approved`) and is unique within its workflow. `IsInitial` marks where requests start and `IsTerminal` marks where they end. `SortOrder` orders states for display. |
| `WorkflowTransition` | An allowed move from one state to another, by state id. `RequiresComment` means the user must explain the move. `IsAutomatic` means the system makes the move, not a user. A pair of states can have at most one transition. |

Example:

```
            ┌──────────► approved (terminal)
draft ──► submitted
(initial)   └──────────► rejected (terminal, RequiresComment)
```

`WorkflowDefinition` exposes `States` and `Transitions` as read-only collections, but it has no methods to add to them yet. Workflows can only be created by inserting rows directly.

## Operations

| Entity | Meaning |
|---|---|
| `Request` | An item moving through a workflow: title, optional description, the workflow it follows, and `CurrentStateCode`. Records who created it and when, and `LastModifiedAt`. |
| `RequestStateHistory` | One row for each move of a request: `FromStateCode` (null for the first entry), `ToStateCode`, who made the move, when, and an optional comment. |

A request refers to its state by **code**, not by state id. That keeps request rows readable and the history stable even if state rows are recreated. The price is that the database cannot enforce that the code exists in the workflow. The application has to check it.

### Intended lifecycle

Only the first step exists in code today.

1. **Create.** The request starts in the workflow's initial state. *Built:* `CreateRequestHandler` validates the input, checks that the workflow exists and saves the request. It takes the initial state code from the caller and does not check it against the workflow, and it does not write a history row yet.
2. **Transition.** A user asks to move the request to another state. The handler loads the workflow, finds a transition from the current state to the target, enforces `RequiresComment`, updates `CurrentStateCode` and `LastModifiedAt`, and writes a `RequestStateHistory` row and an `AuditLog` row in the same transaction. *Not built.*
3. **Automatic moves.** Transitions with `IsAutomatic` are made by the system, for example right after a request enters a state. *Not built.*
4. **Close.** Once a request reaches a terminal state, it accepts no more transitions. *Not built.*

## Identity

| Entity | Meaning |
|---|---|
| `User` | A person in a tenant. The id is **passed in, not generated**: it is the `sub` of the user's Heimdall access token. Email is unique within a tenant. |
| `Role` | A named role in a tenant. `Code` is unique within the tenant. |
| `UserRole` | Joins users to roles. It has no `Id` or `TenantId` of its own. |

Nothing reads or creates users or roles yet. The signed-in user comes from the token (`ICurrentUser`), not from this table. They are meant for "who may make this transition" rules.

## Audit

| Entity | Meaning |
|---|---|
| `AuditLog` | An append-only record: entity type and id, action, who, when, and `Data` (free text, meant for a JSON snapshot or diff). |

Nothing writes audit rows yet.

## Application layer conventions

- One folder per use case under `Application/<Area>/<UseCase>/`: a command or query record, a handler, a result record and, if needed, a static validator.
- Handlers are plain classes registered in `Application/Extensions/ServiceCollectionExtensions.cs`. Controllers call `Handle(command, cancellationToken)` directly.
- Validation errors are thrown as `ValidationException` (a list of messages, mapped to 400). Missing entities are thrown as `NotFoundException` (mapped to 404).
- Repository interfaces live in Application. Implementations live in `Infrastructure/Persistence/Repositories/<Area>/`.
- API contracts (`Api/Contracts/...`) are separate records from the Application commands and results. Controllers map between them.
