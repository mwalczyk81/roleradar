namespace RoleRadar.Domain.Tracking;

public sealed record Compensation(
    decimal? Min = null,
    decimal? Max = null,
    string Currency = "USD",
    string? Text = null);

public sealed record DesiredSalary(
    decimal? Min = null,
    decimal? Max = null,
    string Currency = "USD");
