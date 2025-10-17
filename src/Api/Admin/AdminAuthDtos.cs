namespace Api.Admin;

public sealed record AdminLoginRequest(string Username, string Password);

public sealed record AdminLoginResponse(string Token, AdminUserDto User);

public sealed record AdminUserDto(Guid Id, string Username, bool IsAdmin, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AdminCreateUserRequest(string Username, string Password, bool IsAdmin);

public sealed record AdminSetAdminRequest(bool IsAdmin);

public sealed record AdminChangePasswordRequest(string Password);
