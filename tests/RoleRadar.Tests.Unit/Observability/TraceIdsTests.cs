using System.Diagnostics;
using System.Text.RegularExpressions;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Feature 014 US2 (FR-021 to FR-027, ADR 0044): the one id every host uses is the bare W3C trace id, 32 lower-case
/// hexadecimal characters and not all zero. It comes from <see cref="Activity.Current"/> when there is one, and is generated
/// otherwise, so no log line or audit entry is ever without an id.
/// </summary>
public sealed class TraceIdsTests
{
    private static readonly Regex TraceIdForm = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private static void AssertIsATraceId(string value)
    {
        Assert.Matches(TraceIdForm, value);
        Assert.NotEqual(new string('0', 32), value);
    }

    [Fact]
    public void Current_IsTheBareTraceIdOfTheCurrentActivity_NotTheFullActivityId()
    {
        using var activity = new Activity("trace-ids-test").Start();

        var id = TraceIds.Current();

        Assert.Equal(activity.TraceId.ToHexString(), id);
        AssertIsATraceId(id);
        Assert.NotEqual(activity.Id, id);
    }

    [Fact]
    public void Current_WithNoActivity_GeneratesAnIdOfTheSameForm_AndADifferentOneEachTime()
    {
        Activity.Current = null;

        var first = TraceIds.Current();
        var second = TraceIds.Current();

        AssertIsATraceId(first);
        AssertIsATraceId(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NewRunId_IsAFreshRandomTraceId_EachCall()
    {
        var first = TraceIds.NewRunId();
        var second = TraceIds.NewRunId();

        AssertIsATraceId(first);
        AssertIsATraceId(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void StartActivity_CarriesTheGivenTraceId_SoLogRecordsOfARunShareIt()
    {
        var runId = TraceIds.NewRunId();

        using var activity = TraceIds.StartActivity("roleradar.test.run", runId);

        Assert.Same(activity, Activity.Current);
        Assert.Equal(runId, activity.TraceId.ToHexString());
        Assert.Equal(runId, TraceIds.Current());
    }
}
