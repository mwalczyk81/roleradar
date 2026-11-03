namespace RoleRadar.Infrastructure.Mongo.Reading;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using RoleRadar.Application.Reading.Models;

public static class ReadModelClassMaps
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        try
        {
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        }
        catch (BsonSerializationException)
        {
            // Already registered
        }

        try
        {
            BsonSerializer.RegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
        }
        catch (BsonSerializationException)
        {
            // Already registered
        }

        var guidSerializer = new GuidSerializer(GuidRepresentation.Standard);

        if (!BsonClassMap.IsClassMapRegistered(typeof(PostingReadModel)))
        {
            BsonClassMap.RegisterClassMap<PostingReadModel>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdProperty(p => p.Id).SetElementName("_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.CompanyId).SetElementName("company_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.CompanyName).SetElementName("company_name");
                cm.MapProperty(p => p.Title).SetElementName("title");
                cm.MapProperty(p => p.RequisitionId).SetElementName("requisition_id");
                cm.MapProperty(p => p.Location).SetElementName("location");
                cm.MapProperty(p => p.Disposition).SetElementName("disposition");
                cm.MapProperty(p => p.ResurfacePolicy).SetElementName("resurface_policy");
                cm.MapProperty(p => p.Score).SetElementName("score");
                cm.MapProperty(p => p.CreatedAt).SetElementName("created_at");
                cm.MapProperty(p => p.UpdatedAt).SetElementName("updated_at");
                cm.MapMember(p => p.OwnerId).SetElementName("owner_id");
                cm.MapMember(p => p.DispositionRank).SetElementName("disposition_rank");
                cm.MapMember(p => p.HasScore).SetElementName("has_score");
            });
        }

        if (!BsonClassMap.IsClassMapRegistered(typeof(CompanyReadModel)))
        {
            BsonClassMap.RegisterClassMap<CompanyReadModel>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdProperty(p => p.Id).SetElementName("_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.Name).SetElementName("name");
                cm.MapProperty(p => p.Status).SetElementName("status");
                cm.MapProperty(p => p.OwnershipType).SetElementName("ownership_type");
                cm.MapProperty(p => p.Headquarters).SetElementName("headquarters");
                cm.MapProperty(p => p.Description).SetElementName("description");
                cm.MapProperty(p => p.CategoryTags).SetElementName("category_tags");
                cm.MapMember(p => p.OwnerId).SetElementName("owner_id");
            });
        }

        if (!BsonClassMap.IsClassMapRegistered(typeof(ApplicationReadModel)))
        {
            BsonClassMap.RegisterClassMap<ApplicationReadModel>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdProperty(p => p.Id).SetElementName("_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.PostingId).SetElementName("posting_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.CompanyId).SetElementName("company_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.PostingTitle).SetElementName("posting_title");
                cm.MapProperty(p => p.CompanyName).SetElementName("company_name");
                cm.MapProperty(p => p.Status).SetElementName("status");
                cm.MapProperty(p => p.AppliedAt).SetElementName("applied_at");
                cm.MapProperty(p => p.HasInterview).SetElementName("has_interview");
                cm.MapProperty(p => p.FirstResponseAt).SetElementName("first_response_at");
                cm.MapProperty(p => p.LastEventAt).SetElementName("last_event_at");
                cm.MapMember(p => p.OwnerId).SetElementName("owner_id");
                cm.MapMember(p => p.HasFirstResponse).SetElementName("has_first_response");
                cm.MapMember(p => p.HasLastEvent).SetElementName("has_last_event");
                cm.UnmapProperty(p => p.AgeInDays);
            });
        }

        if (!BsonClassMap.IsClassMapRegistered(typeof(NoteReadModel)))
        {
            BsonClassMap.RegisterClassMap<NoteReadModel>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdProperty(p => p.Id).SetElementName("_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.Heading).SetElementName("title");
                cm.MapProperty(p => p.Tags).SetElementName("tags");
                // Notes store no polymorphic link; the REST NoteDto reports the stored area as the linked type
                // and no linked id. Same here, so the two read paths agree and no backfill is needed.
                cm.MapProperty(p => p.LinkedEntityType).SetElementName("area");
                cm.UnmapProperty(p => p.LinkedEntityId);
                cm.MapProperty(p => p.CreatedAt).SetElementName("created_at");
                cm.MapProperty(p => p.UpdatedAt).SetElementName("updated_at");
                cm.MapProperty(p => p.Content).SetElementName("body");
                cm.MapMember(p => p.OwnerId).SetElementName("owner_id");
            });
        }

        if (!BsonClassMap.IsClassMapRegistered(typeof(CriterionReadModel)))
        {
            BsonClassMap.RegisterClassMap<CriterionReadModel>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdProperty(p => p.Id).SetElementName("_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.Name).SetElementName("key");
                cm.MapProperty(p => p.Category).SetElementName("category");
                cm.MapProperty(p => p.RuleText).SetElementName("value");
                cm.MapProperty(p => p.EffectiveFrom).SetElementName("effective_from");
                cm.MapProperty(p => p.EffectiveTo).SetElementName("effective_to");
                cm.MapProperty(p => p.IsActive).SetElementName("is_active");
                cm.MapMember(p => p.OwnerId).SetElementName("owner_id");
                cm.MapMember(p => p.HasEffectiveTo).SetElementName("has_effective_to");
            });
        }

        if (!BsonClassMap.IsClassMapRegistered(typeof(SearchRunReadModel)))
        {
            BsonClassMap.RegisterClassMap<SearchRunReadModel>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdProperty(p => p.Id).SetElementName("_id").SetSerializer(guidSerializer);
                cm.MapProperty(p => p.RunDate).SetElementName("started_at");
                cm.MapProperty(p => p.NewPostings).SetElementName("new_postings_count");
                cm.MapProperty(p => p.Summary).SetElementName("notes");
                cm.UnmapProperty(p => p.CompaniesSearched);
                cm.UnmapProperty(p => p.PostingsFound);
                cm.UnmapProperty(p => p.ErrorsCount);
                cm.MapMember(p => p.OwnerId).SetElementName("owner_id");
            });
        }
    }
}
