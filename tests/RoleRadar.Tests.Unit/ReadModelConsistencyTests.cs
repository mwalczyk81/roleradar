namespace RoleRadar.Tests.Unit;

using MongoDB.Bson;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using RoleRadar.Infrastructure.Mongo.Repositories;
using Xunit;

public sealed class ReadModelConsistencyTests
{
    private static Posting CreateSamplePosting(PostingDisposition disposition, decimal? score = null)
    {
        return Posting.Create(
            ownerId: "owner-test-1",
            companyId: Guid.NewGuid(),
            title: "Software Engineer",
            disposition: disposition,
            score: score);
    }

    [Theory]
    [InlineData(PostingDisposition.Unscored, 0)]
    [InlineData(PostingDisposition.Lead, 1)]
    [InlineData(PostingDisposition.Scored, 2)]
    [InlineData(PostingDisposition.Applied, 3)]
    [InlineData(PostingDisposition.Rejected, 4)]
    [InlineData(PostingDisposition.ScreenedOut, 5)]
    [InlineData(PostingDisposition.Closed, 6)]
    public void SerializePosting_Computes_Correct_DispositionRank(PostingDisposition disposition, int expectedRank)
    {
        var posting = CreateSamplePosting(disposition);
        var doc = MongoPostingRepository.SerializePosting(posting);

        Assert.True(doc.Contains("disposition_rank"), "Persisted posting BSON document must contain 'disposition_rank'");
        Assert.Equal(expectedRank, doc["disposition_rank"].AsInt32);
    }

    [Fact]
    public void SerializePosting_Maintains_HasScore_True_When_Score_Present()
    {
        var posting = CreateSamplePosting(PostingDisposition.Scored, score: 8.5m);
        var doc = MongoPostingRepository.SerializePosting(posting);

        Assert.True(doc.Contains("has_score"), "Persisted posting BSON document must contain 'has_score'");
        Assert.True(doc["has_score"].AsBoolean);
    }

    [Fact]
    public void SerializePosting_Maintains_HasScore_False_When_Score_Null()
    {
        var posting = CreateSamplePosting(PostingDisposition.Unscored, score: null);
        var doc = MongoPostingRepository.SerializePosting(posting);

        Assert.True(doc.Contains("has_score"), "Persisted posting BSON document must contain 'has_score'");
        Assert.False(doc["has_score"].AsBoolean);
    }

    [Theory]
    [InlineData(PostingDisposition.Unscored)]
    [InlineData(PostingDisposition.Lead)]
    [InlineData(PostingDisposition.Scored)]
    [InlineData(PostingDisposition.Applied)]
    [InlineData(PostingDisposition.Rejected)]
    [InlineData(PostingDisposition.ScreenedOut)]
    [InlineData(PostingDisposition.Closed)]
    public void MapDispositionRank_RepositoryAndReconcile_AgreeForAll7Dispositions(PostingDisposition disposition)
    {
        var repoRank = MongoPostingRepository.MapDispositionRank(disposition);
        var reconcileRank = ReadModelReconciliationService.MapDispositionRank(disposition.ToSnakeCase());

        Assert.Equal(repoRank, reconcileRank);
    }

    [Fact]
    public void MapDispositionRank_ThrowsOnUnknownDisposition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MongoPostingRepository.MapDispositionRank((PostingDisposition)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadModelReconciliationService.MapDispositionRank("unknown_bogus_disposition"));
    }
}
