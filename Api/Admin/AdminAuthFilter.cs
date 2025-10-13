using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Api.Admin;

public sealed class AdminAuthFilter : IEndpointFilter
{
    public const string ContextItemKey = "DocDuck.AdminUser";

    private readonly AdminAuthService _authService;
    private readonly AdminUserStore _userStore;
    private readonly ILogger<AdminAuthFilter> _logger;

    public AdminAuthFilter(AdminAuthService authService, AdminUserStore userStore, ILogger<AdminAuthFilter> logger)
    {
        _authService = authService;
        _userStore = userStore;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!httpContext.Request.Headers.TryGetValue("Authorization", out var headerValue))
        {
            return Results.Unauthorized();
        }

        var header = headerValue.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Unauthorized();
        }

        var token = header[7..].Trim();
        if (!_authService.TryParseToken(token, out var payload))
        {
            _logger.LogWarning("Admin token validation failed: signature or expiry invalid.");
            return Results.Unauthorized();
        }

        var user = await _userStore.GetByIdAsync(payload.UserId, httpContext.RequestAborted);
        if (user is null)
        {
            _logger.LogWarning("Admin token validation failed: user {UserId} not found.", payload.UserId);
            return Results.Unauthorized();
        }

        if (!user.IsAdmin)
        {
            _logger.LogWarning("Admin token validation failed: user {UserId} lacks admin privileges.", payload.UserId);
            return Results.Unauthorized();
        }

        httpContext.Items[ContextItemKey] = new AdminUser(user.Id, user.Username, user.IsAdmin, user.CreatedAt, user.UpdatedAt, null);
        return await next(context);
    }
}
