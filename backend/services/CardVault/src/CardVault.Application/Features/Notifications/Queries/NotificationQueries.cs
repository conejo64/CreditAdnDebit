using CardVault.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace CardVault.Application.Features.Notifications.Queries;

public record ListCustomerNotificationsQuery(Guid? CustomerId, Guid? AccountId, string? Type, int Take) : IRequest<IResult>;

public class ListCustomerNotificationsQueryHandler : IRequestHandler<ListCustomerNotificationsQuery, IResult>
{
    private readonly NotificationService _service;

    public ListCustomerNotificationsQueryHandler(NotificationService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(ListCustomerNotificationsQuery request, CancellationToken cancellationToken)
    {
        var items = await _service.ListAsync(request.CustomerId, request.AccountId, request.Type, request.Take, cancellationToken);
        return Results.Ok(items);
    }
}

public record GetCustomerNotificationQuery(Guid NotificationId) : IRequest<IResult>;

public class GetCustomerNotificationQueryHandler : IRequestHandler<GetCustomerNotificationQuery, IResult>
{
    private readonly NotificationService _service;

    public GetCustomerNotificationQueryHandler(NotificationService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetCustomerNotificationQuery request, CancellationToken cancellationToken)
    {
        var item = await _service.GetAsync(request.NotificationId, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
}
