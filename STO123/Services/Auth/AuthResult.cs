namespace STO123.Services.Auth;

public sealed record AuthResult<T>(int StatusCode, T? Value = default, string? Error = null)
{
    public static AuthResult<T> Success(int statusCode, T value) => new(statusCode, value);
    public static AuthResult<T> Failure(int statusCode, string error) => new(statusCode, default, error);
}
