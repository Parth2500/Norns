namespace CoreOps.Api.Contracts.Operations.Requests;

public sealed record CreateRequestRequest(
    string Title,
    string? Description,
    Guid WorkflowDefinitionId,
    string InitialStateCode
);
