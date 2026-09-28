namespace STO123.Services.Auth;

public static class PasswordRules
{
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return "Password must be at least 8 characters long.";

        if (!password.Any(char.IsLetter))
            return "Password must contain at least one letter.";

        if (!password.Any(char.IsDigit))
            return "Password must contain at least one digit.";

        return null;
    }
}
