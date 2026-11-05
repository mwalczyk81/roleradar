using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

/// <summary>
/// Feature 008 phase 2: the rejection sync on a real MongoDB replica set (isolated Testcontainers only), through
/// the real handler decorator. Fictional data only.
/// </summary>
public sealed class PostingRejectionSyncIntegrationTests(SharedMongoFixture sharedMongo)
{
    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    // ---------- T024: derived fields come from the single repository choke point ----------

    [Fact]
    public async Task DerivedApplicationFields_AfterAPostingDrivenRejection_EqualThoseOfAManuallyRejectedTwin()
    {
        await using var host = await PostingRejectionSyncHost.StartAsync(_sharedMongo, "rrtest_reject_sync_derived");
        var viaPosting = await host.SeedAppliedPostingAsync("Staff Platform Engineer");
        var viaManualEvent = await host.SeedAppliedPostingAsync("Principal Platform Engineer");

        using (var scope = host.Services.CreateScope())
        {
            var transition = await host.TransitionHandler(scope).HandleAsync(new TransitionPostingCommand(
                PostingId: viaPosting.PostingId,
                TargetDisposition: "rejected",
                VersionToken: viaPosting.VersionToken,
                Reason: "Rejected by email",
                EventDate: "2026-09-15",
                Actor: "integration-test"));
            Assert.True(transition.IsSuccess, transition.Error?.Message);

            var manual = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>>()
                .HandleAsync(new RecordApplicationCommand(
                    PostingId: viaManualEvent.PostingId,
                    Action: "append_event",
                    Event: new ApplicationEventInputDto("rejected", "2026-09-15", "web", "Rejected by email")));
            Assert.True(manual.IsSuccess, manual.Error?.Message);
        }

        var a = await host.ApplicationDocAsync(viaPosting.ApplicationId);
        var b = await host.ApplicationDocAsync(viaManualEvent.ApplicationId);
        string[] derived = ["outcome", "has_first_response", "has_rejection", "rejected_before_interview", "has_interview", "has_offer", "has_last_event", "last_event_at"];
        foreach (var field in derived)
        {
            Assert.True(a.Contains(field), $"posting-driven application is missing '{field}'");
            Assert.Equal(b[field], a[field]);
        }

        Assert.Equal("rejected", a["outcome"].AsString);
        Assert.True(a["has_rejection"].AsBoolean);
        Assert.True(a["has_first_response"].AsBoolean);
        Assert.Equal(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), a["last_event_at"].ToUniversalTime());

        var events = await host.EventDocsAsync(viaPosting.ApplicationId);
        var rejected = Assert.Single(events, e => e["event_type"].AsString == "rejected");
        Assert.Equal("posting_transition", rejected["event_source"].AsString);
        Assert.Equal("2026-09-15", rejected["event_date"].AsString);
    }

    // ---------- T025: one audit set, one correlation id ----------

    [Fact]
    public async Task Audit_AfterTheTransition_HoldsExactlyOnePostingEntryAndOneApplicationEntry_WithEqualCorrelationId()
    {
        await using var host = await PostingRejectionSyncHost.StartAsync(_sharedMongo, "rrtest_reject_sync_audit");
        var seeded = await host.SeedAppliedPostingAsync();

        using (var scope = host.Services.CreateScope())
        {
            var result = await host.TransitionHandler(scope).HandleAsync(new TransitionPostingCommand(
                PostingId: seeded.PostingId,
                TargetDisposition: "rejected",
                VersionToken: seeded.VersionToken,
                Reason: "Rejected by email",
                EventDate: "2026-09-15",
                Actor: "integration-test"));
            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        var audits = await host.AuditDocsAsync();
        var postingEntry = Assert.Single(audits, a =>
            a["operation"].AsString == "posting_transition" && a["entity_id"].AsGuid == seeded.PostingId);
        var applicationEntry = Assert.Single(audits, a =>
            a["operation"].AsString == "application_event_append" && a["entity_id"].AsGuid == seeded.ApplicationId);

        var postingCorrelation = postingEntry["details"]["after"]["correlation_id"].AsString;
        var applicationCorrelation = applicationEntry["details"]["after"]["correlation_id"].AsString;
        Assert.False(string.IsNullOrWhiteSpace(postingCorrelation));
        Assert.Equal(postingCorrelation, applicationCorrelation);
        Assert.Equal("posting_transition", applicationEntry["details"]["after"]["event_source"].AsString);
        Assert.Equal("integration-test", postingEntry["actor"].AsString);
        Assert.Equal("integration-test", applicationEntry["actor"].AsString);
    }

    // ---------- T026: atomicity through the real decorator ----------

    [Theory]
    [InlineData(ApplicationAppendFault.ThrowAfterPostingWrite)]
    [InlineData(ApplicationAppendFault.ReturnFailedResult)]
    public async Task FailureWhileWritingTheApplicationEvent_RollsBackThePostingTheApplicationAndTheAudit(ApplicationAppendFault fault)
    {
        await using var host = await PostingRejectionSyncHost.StartAsync(_sharedMongo, "rrtest_reject_sync_atomic");
        var seeded = await host.SeedAppliedPostingAsync();

        var postingBefore = await host.PostingDocAsync(seeded.PostingId);
        var applicationBefore = await host.ApplicationDocAsync(seeded.ApplicationId);
        var eventCountBefore = (await host.EventDocsAsync(seeded.ApplicationId)).Count;
        var auditCountBefore = (await host.AuditDocsAsync()).Count;

        host.Faults.Mode = fault;
        Exception? thrown = null;
        Result<PostingTransitionResultDto>? result = null;
        using (var scope = host.Services.CreateScope())
        {
            try
            {
                result = await host.TransitionHandler(scope).HandleAsync(new TransitionPostingCommand(
                    PostingId: seeded.PostingId,
                    TargetDisposition: "rejected",
                    VersionToken: seeded.VersionToken,
                    Reason: "Rejected by email",
                    EventDate: "2026-09-15",
                    Actor: "integration-test"));
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }

        host.Faults.Mode = ApplicationAppendFault.None;

        // The failure must surface to the caller, either as an exception or as a failed Result.
        Assert.True(
            thrown is not null || (result is { IsSuccess: false }),
            $"Fault {fault} did not surface: neither an exception nor a failed result.");

        var postingAfter = await host.PostingDocAsync(seeded.PostingId);
        Assert.Equal("applied", postingAfter["disposition"].AsString);
        Assert.Equal(postingBefore["version_token"], postingAfter["version_token"]);
        Assert.Equal(postingBefore["disposition"], postingAfter["disposition"]);

        var applicationAfter = await host.ApplicationDocAsync(seeded.ApplicationId);
        Assert.Equal("applied", applicationAfter["outcome"].AsString);
        Assert.Equal(applicationBefore["version_token"], applicationAfter["version_token"]);

        var events = await host.EventDocsAsync(seeded.ApplicationId);
        Assert.Equal(eventCountBefore, events.Count);
        Assert.DoesNotContain(events, e => e["event_type"].AsString == "rejected");

        Assert.Equal(auditCountBefore, (await host.AuditDocsAsync()).Count);

        // Nothing is stuck: the same command, with the same version token, now succeeds exactly once.
        using var retryScope = host.Services.CreateScope();
        var retry = await host.TransitionHandler(retryScope).HandleAsync(new TransitionPostingCommand(
            PostingId: seeded.PostingId,
            TargetDisposition: "rejected",
            VersionToken: seeded.VersionToken,
            Reason: "Rejected by email",
            EventDate: "2026-09-15",
            Actor: "integration-test"));
        Assert.True(retry.IsSuccess, retry.Error?.Message);
        Assert.Single(await host.EventDocsAsync(seeded.ApplicationId), e => e["event_type"].AsString == "rejected");
    }

    // ---------- T027: idempotency ----------

    [Fact]
    public async Task RepeatingThePostingTransition_LeavesExactlyOneRejectedEvent()
    {
        await using var host = await PostingRejectionSyncHost.StartAsync(_sharedMongo, "rrtest_reject_sync_repeat");
        var seeded = await host.SeedAppliedPostingAsync();

        using var scope = host.Services.CreateScope();
        var handler = host.TransitionHandler(scope);
        var first = await handler.HandleAsync(new TransitionPostingCommand(
            seeded.PostingId, "rejected", seeded.VersionToken, Reason: "Rejected by email", Actor: "integration-test"));
        Assert.True(first.IsSuccess, first.Error?.Message);

        var again = await handler.HandleAsync(new TransitionPostingCommand(
            seeded.PostingId, "rejected", first.Value!.VersionToken, Reason: "Rejected by email", Actor: "integration-test"));
        Assert.False(again.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, again.Error!.Value.Code);

        var staleReplay = await handler.HandleAsync(new TransitionPostingCommand(
            seeded.PostingId, "rejected", seeded.VersionToken, Reason: "Rejected by email", Actor: "integration-test"));
        Assert.False(staleReplay.IsSuccess);

        Assert.Single(await host.EventDocsAsync(seeded.ApplicationId), e => e["event_type"].AsString == "rejected");
    }

    [Fact]
    public async Task RejectingTheApplicationFirst_ThenThePosting_LeavesExactlyOneRejectedEvent()
    {
        await using var host = await PostingRejectionSyncHost.StartAsync(_sharedMongo, "rrtest_reject_sync_app_first");
        var seeded = await host.SeedAppliedPostingAsync();

        using var scope = host.Services.CreateScope();
        var manual = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>>()
            .HandleAsync(new RecordApplicationCommand(
                PostingId: seeded.PostingId,
                Action: "append_event",
                Event: new ApplicationEventInputDto("rejected", "2026-09-12", "web", "Rejected by email")));
        Assert.True(manual.IsSuccess, manual.Error?.Message);

        var posting = await host.PostingDocAsync(seeded.PostingId);
        Assert.Equal("rejected", posting["disposition"].AsString);

        var transition = await host.TransitionHandler(scope).HandleAsync(new TransitionPostingCommand(
            seeded.PostingId, "rejected", posting["version_token"].AsGuid, Reason: "Rejected by email", Actor: "integration-test"));
        Assert.False(transition.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, transition.Error!.Value.Code);

        var rejected = Assert.Single(await host.EventDocsAsync(seeded.ApplicationId), e => e["event_type"].AsString == "rejected");
        Assert.Equal("web", rejected["event_source"].AsString);
    }
}
