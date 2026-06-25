using CardVault.Application.Features.Delinquency.Commands;
using CardVault.Application.Features.Delinquency.Queries;
using CardVault.Infrastructure.Persistence.Collections;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

/// <summary>
/// Collections endpoint: read-only view of delinquent accounts (v76)
/// plus mutation operations for contact attempts and internal notes (v77).
/// </summary>
[ApiController]
[Route("api/collections")]
[Authorize(Policy = "CanViewCollections")]
public class DelinquencyController : ControllerBase
{
    private readonly IMediator _mediator;

    public DelinquencyController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Returns a paginated list of delinquent accounts.
    /// Optional filter: <c>bucket</c> (1–4) and <c>status</c> (Active|Resolved).
    /// </summary>
    [HttpGet("delinquencies")]
    public async Task<IResult> GetDelinquencies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? bucket = null,
        [FromQuery] string status = "Active",
        CancellationToken ct = default)
    {
        return await _mediator.Send(new GetDelinquentAccountsQuery
        {
            Page     = page,
            PageSize = pageSize,
            Bucket   = bucket,
            Status   = status,
        }, ct);
    }

    // ─────────────────────────────────────────────
    // v77 — Mutation endpoints
    // ─────────────────────────────────────────────

    /// <summary>
    /// Registers a contact attempt for the specified delinquency record.
    /// Requires <c>CanManageCollections</c> policy (Admin, Operator, or collections:manage claim).
    /// Returns 201 with the new attempt ID on success, 422 if the record is resolved.
    /// </summary>
    [HttpPost("delinquencies/{id:guid}/contact-attempts")]
    [Authorize(Policy = "CanManageCollections")]
    public async Task<IActionResult> RegisterContactAttempt(
        Guid id,
        [FromBody] RegisterContactAttemptRequest body,
        CancellationToken ct = default)
    {
        var userId = User.Identity?.Name ?? "unknown";
        try
        {
            var attemptId = await _mediator.Send(new RegisterContactAttemptCommand(
                DelinquencyRecordId: id,
                Channel:             body.Channel,
                Outcome:             body.Outcome,
                Notes:               body.Notes,
                AttemptedBy:         userId
            ), ct);

            return CreatedAtAction(nameof(GetContactAttempts), new { id }, new { id = attemptId });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("resolved"))
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Adds an internal note to the specified delinquency record.
    /// Requires <c>CanManageCollections</c> policy.
    /// Returns 201 with the new note ID on success, 422 if the record is resolved.
    /// </summary>
    [HttpPost("delinquencies/{id:guid}/notes")]
    [Authorize(Policy = "CanManageCollections")]
    public async Task<IActionResult> AddNote(
        Guid id,
        [FromBody] AddNoteRequest body,
        CancellationToken ct = default)
    {
        var userId = User.Identity?.Name ?? "unknown";
        try
        {
            var noteId = await _mediator.Send(new AddDelinquencyNoteCommand(
                DelinquencyRecordId: id,
                Content:             body.Content,
                CreatedBy:           userId
            ), ct);

            return CreatedAtAction(nameof(GetNotes), new { id }, new { id = noteId });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("resolved"))
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns all contact attempts for the specified delinquency record, sorted newest first.
    /// Requires only <c>CanViewCollections</c> (inherited from controller-level policy).
    /// </summary>
    [HttpGet("delinquencies/{id:guid}/contact-attempts")]
    public async Task<IActionResult> GetContactAttempts(Guid id, CancellationToken ct = default)
    {
        var items = await _mediator.Send(new GetContactAttemptsQuery(id), ct);
        return Ok(items.Select(a => new
        {
            a.Id,
            a.DelinquencyRecordId,
            a.Channel,
            a.Outcome,
            a.Notes,
            AttemptedBy = a.AttemptedBy,
            AttemptedOn = a.AttemptedOn,
        }));
    }

    /// <summary>
    /// Returns all internal notes for the specified delinquency record, sorted newest first.
    /// Requires only <c>CanViewCollections</c> (inherited from controller-level policy).
    /// </summary>
    [HttpGet("delinquencies/{id:guid}/notes")]
    public async Task<IActionResult> GetNotes(Guid id, CancellationToken ct = default)
    {
        var items = await _mediator.Send(new GetDelinquencyNotesQuery(id), ct);
        return Ok(items.Select(n => new
        {
            n.Id,
            n.DelinquencyRecordId,
            n.Content,
            CreatedBy = n.CreatedBy,
            CreatedOn = n.CreatedOn,
        }));
    }
}

// ─────────────────────────────────────────────
// Request DTOs (local to controller — no need for separate files)
// ─────────────────────────────────────────────

public sealed record RegisterContactAttemptRequest(
    ContactChannel Channel,
    ContactOutcome Outcome,
    string? Notes
);

public sealed record AddNoteRequest(string Content);
