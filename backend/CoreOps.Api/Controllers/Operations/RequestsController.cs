using CoreOps.Api.Contracts.Operations.Requests;
using CoreOps.Application.Common.Security;
using CoreOps.Application.Operations.CreateRequest;
using CoreOps.Application.Operations.GetRequest;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoreOps.Api.Controllers.Operations;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class RequestsController : ControllerBase
{
    private readonly CreateRequestHandler _handler;
    private readonly GetRequestByIdQueryHandler _getRequestByIdHandler;
    private readonly ICurrentUser _currentUser;

    public RequestsController(
        CreateRequestHandler handler, 
        GetRequestByIdQueryHandler getRequestByIdHandler,
        ICurrentUser currentUser
    ) {
        _handler = handler;
        _getRequestByIdHandler = getRequestByIdHandler;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<ActionResult<CreateRequestResponse>> Create(
        [FromBody] CreateRequestRequest request,
        CancellationToken cancellationToken
    ) {
        var command = new CreateRequestCommand(
            TenantId: _currentUser.TenantId,
            Title: request.Title,
            Description: request.Description,
            WorkflowDefinitionId: request.WorkflowDefinitionId,
            InitialStateCode: request.InitialStateCode,
            CreatedBy: _currentUser.UserId
        );

        var result = await _handler.Handle(command, cancellationToken);

        return Ok(new CreateRequestResponse(result.RequestId));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetRequestResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken
    ) {
        var result = await _getRequestByIdHandler.Handle(new GetRequestByIdQuery(id), cancellationToken);

        var response = new GetRequestResponse(
            result.Id,
            result.TenantId,
            result.Title,
            result.Description,
            result.WorkflowDefinitionId,
            result.CurrentStateCode,
            result.CreatedBy,
            result.CreatedAt,
            result.LastModifiedAt);

        return Ok(response);
    }
}
