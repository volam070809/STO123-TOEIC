namespace STO123.Services.Arena;

public sealed class ArenaProblem(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
