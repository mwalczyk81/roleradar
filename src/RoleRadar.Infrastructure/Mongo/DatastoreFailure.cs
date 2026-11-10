using MongoDB.Driver;

namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Feature 013 (FR-024): the one place that decides whether an exception means "the data store did not answer" (the owner
/// can retry; the API answers 503) as opposed to a rejected write or a defect. A connection failure, a server-selection or
/// operation timeout and a lost primary all count. A duplicate-key write, a bad argument, an invalid state and a caller's
/// own cancellation do not. The cause is looked for inside wrapped and aggregate exceptions too, because the driver and the
/// repositories often wrap it.
/// </summary>
public static class DatastoreFailure
{
    public static bool IsUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AggregateException aggregate)
            {
                if (aggregate.InnerExceptions.Any(IsUnavailable))
                {
                    return true;
                }

                continue;
            }

            if (current is MongoConnectionException
                or MongoNotPrimaryException
                or MongoNodeIsRecoveringException
                or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
