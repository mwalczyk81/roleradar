namespace RoleRadar.Domain.Tracking;

public class TrackingIndexValidationException : Exception
{
    public string IndexName { get; }

    public TrackingIndexValidationException(string indexName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        IndexName = indexName ?? string.Empty;
    }
}
