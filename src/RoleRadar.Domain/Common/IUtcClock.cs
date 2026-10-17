namespace RoleRadar.Domain.Common;

public interface IUtcClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemUtcClock : IUtcClock
{
    public static readonly SystemUtcClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;
}
