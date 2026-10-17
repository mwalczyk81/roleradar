namespace RoleRadar.Domain.Common;

public readonly record struct OwnerId
{
    public const string DefaultValue = "local-owner";

    public static readonly OwnerId Default = new(DefaultValue);

    public string Value { get; }

    public OwnerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Owner identifier cannot be null or whitespace.", nameof(value));
        }

        Value = value.Trim();
    }

    public override string ToString() => Value;
}
