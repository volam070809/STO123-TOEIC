using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class AdminDateRangeValidationTests
{
    [Fact]
    public void Sql_datetime_boundaries_are_explicit_and_safe_for_exclusive_end()
    {
        Assert.Null(AdminDateRangeValidation.Error(
            new DateTime(1753, 1, 1), new DateTime(1753, 1, 1)));
        Assert.Null(AdminDateRangeValidation.Error(
            new DateTime(9999, 12, 30), new DateTime(9999, 12, 30)));
        Assert.Equal(new DateTime(9999, 12, 31),
            AdminDateRangeValidation.ExclusiveEnd(new DateTime(9999, 12, 30)));
        Assert.NotNull(AdminDateRangeValidation.Error(
            new DateTime(1752, 12, 31), new DateTime(1753, 1, 1)));
        Assert.NotNull(AdminDateRangeValidation.Error(
            new DateTime(9999, 12, 31), new DateTime(9999, 12, 31)));
    }

    [Fact]
    public void Incomplete_reversed_and_excessive_ranges_are_rejected()
    {
        Assert.NotNull(AdminDateRangeValidation.Error(new DateTime(2026, 10, 10), null));
        Assert.NotNull(AdminDateRangeValidation.Error(null, new DateTime(2026, 10, 17)));
        Assert.NotNull(AdminDateRangeValidation.Error(
            new DateTime(2026, 10, 18), new DateTime(2026, 10, 17)));
        Assert.NotNull(AdminDateRangeValidation.Error(
            new DateTime(2026, 1, 1), new DateTime(2026, 4, 1), 90));
    }

    [Fact]
    public void Full_calendar_year_including_leap_year_is_allowed_but_367_days_is_not()
    {
        Assert.Null(AdminDateRangeValidation.Error(
            new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), 366));
        Assert.Null(AdminDateRangeValidation.Error(
            new DateTime(2025, 1, 1), new DateTime(2025, 12, 31), 366));
        Assert.NotNull(AdminDateRangeValidation.Error(
            new DateTime(2024, 1, 1), new DateTime(2025, 1, 1), 366));
    }
}
