namespace STO123.Services.Admin;

public static class AdminDateRangeValidation
{
    public static readonly DateTime SqlDateTimeMinimum = new(1753, 1, 1);
    public static readonly DateTime MaximumInclusiveEnd = new(9999, 12, 30);

    public static string? Error(DateTime? dateFrom, DateTime? dateTo, int? maxDays = null)
    {
        if (dateFrom.HasValue != dateTo.HasValue)
            return "Vui lòng nhập đầy đủ Từ ngày và Đến ngày.";
        if (!dateFrom.HasValue) return null;

        var from = dateFrom.Value.Date;
        var to = dateTo!.Value.Date;
        if (from < SqlDateTimeMinimum || from > MaximumInclusiveEnd ||
            to < SqlDateTimeMinimum || to > MaximumInclusiveEnd)
            return "Ngày phải nằm trong phạm vi 01/01/1753 đến 30/12/9999.";
        if (from > to) return "Ngày bắt đầu không được sau ngày kết thúc.";
        if (maxDays.HasValue && (to - from).TotalDays + 1 > maxDays.Value)
            return $"Khoảng ngày không được vượt quá {maxDays.Value} ngày.";
        return null;
    }

    public static DateTime ExclusiveEnd(DateTime dateTo) => dateTo.Date.AddDays(1);
}
