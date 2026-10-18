namespace RoleRadar.Infrastructure.Mongo;

public class TrackingIndexValidationException : RoleRadar.Domain.Tracking.TrackingIndexValidationException
{
    public TrackingIndexValidationException(string indexName, string message, Exception? innerException = null)
        : base(indexName, message, innerException)
    {
    }
}
