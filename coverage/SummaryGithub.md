# Summary
<details open><summary>Summary</summary>

|||
|:---|:---|
| Generated on: | 11/14/2026 - 13:06:10 |
| Coverage date: | 11/14/2026 - 12:56:34 - 11/14/2026 - 13:04:03 |
| Parser: | MultiReport (8x Cobertura) |
| Assemblies: | 2 |
| Classes: | 192 |
| Files: | 88 |
| **Line coverage:** | 92.6% (7686 of 8294) |
| Covered lines: | 7686 |
| Uncovered lines: | 608 |
| Coverable lines: | 8294 |
| Total lines: | 14999 |
| **Branch coverage:** | 77.7% (3323 of 4276) |
| Covered branches: | 3323 |
| Total branches: | 4276 |
| **Method coverage:** | [Feature is only available for sponsors](https://reportgenerator.io/pro) |

</details>

## Coverage
<details><summary>RoleRadar.Application - 92.1%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**RoleRadar.Application**|**92.1%**|**75.4%**|
|RoleRadar.Application.Abstractions.BackupArchiveSummaryDto|100%||
|RoleRadar.Application.Abstractions.CommandActor|100%|100%|
|RoleRadar.Application.Abstractions.ITrackingTransactionContext|25%||
|RoleRadar.Application.Abstractions.NoteSearchCriteria|100%||
|RoleRadar.Application.Abstractions.NoteSearchHit|100%||
|RoleRadar.Application.Abstractions.NoteWriteOutcome|100%||
|RoleRadar.Application.Abstractions.NullRequestContext|100%||
|RoleRadar.Application.Abstractions.RestoreArchiveSummaryDto|100%||
|RoleRadar.Application.Abstractions.Result`1|100%||
|RoleRadar.Application.Abstractions.ResultError|100%||
|RoleRadar.Application.Abstractions.StoreReadinessStatus|100%||
|RoleRadar.Application.Abstractions.UpsertImportedSectionRequest|100%||
|RoleRadar.Application.Abstractions.WipeSummaryDto|100%||
|RoleRadar.Application.Audit.Queries.AuditCursorCodec|96%|60%|
|RoleRadar.Application.Audit.Queries.AuditEntityTypes|100%|50%|
|RoleRadar.Application.Audit.Queries.AuditListItem|100%||
|RoleRadar.Application.Audit.Queries.AuditListPage|100%||
|RoleRadar.Application.Audit.Queries.ListAuditEntriesQuery|100%||
|RoleRadar.Application.Audit.Queries.ListAuditEntriesQueryHandler|100%|75%|
|RoleRadar.Application.Audit.Queries.ListAuditEntriesQueryValidator|100%|100%|
|RoleRadar.Application.Decorators.LoggingCommandDecorator`2|100%|75%|
|RoleRadar.Application.Decorators.LoggingQueryDecorator`2|100%|75%|
|RoleRadar.Application.Decorators.TransactionCommandDecorator`2|100%|100%|
|RoleRadar.Application.DependencyInjection|100%||
|RoleRadar.Application.Import.Commands.ImportMarkdownCommand|100%||
|RoleRadar.Application.Import.Commands.ImportMarkdownCommandHandler|93.7%|90.4%|
|RoleRadar.Application.Import.Commands.ImportMarkdownCommandValidator|98%|94.4%|
|RoleRadar.Application.Import.Commands.ImportMarkdownConflictDto|80%||
|RoleRadar.Application.Import.Commands.ImportMarkdownOrphanDto|75%||
|RoleRadar.Application.Import.Commands.ImportMarkdownSummaryDto|100%||
|RoleRadar.Application.Insights.GetInsightsStatsQuery|100%||
|RoleRadar.Application.Insights.GetInsightsStatsQueryHandler|100%|87.5%|
|RoleRadar.Application.Insights.InsightsStatsCalculator|100%|100%|
|RoleRadar.Application.Insights.InsightsStatsDto|100%||
|RoleRadar.Application.Insights.InsightsWindowBounds|100%|100%|
|RoleRadar.Application.Insights.InsightsWindows|92.3%|83.3%|
|RoleRadar.Application.Insights.ResponseTimeDto|100%||
|RoleRadar.Application.Insights.SearchRunYieldDto|100%||
|RoleRadar.Application.Insights.SearchRunYieldPointDto|100%||
|RoleRadar.Application.Insights.WeeklyApplicationsDto|100%||
|RoleRadar.Application.Integrity.IntegrityAnomalyDto|0%||
|RoleRadar.Application.Integrity.VerifyIntegrityQuery|100%||
|RoleRadar.Application.Integrity.VerifyIntegrityQueryHandler|100%|50%|
|RoleRadar.Application.Integrity.VerifyIntegrityResultDto|100%||
|RoleRadar.Application.Notes.Commands.AppendToNoteCommand|100%||
|RoleRadar.Application.Notes.Commands.AppendToNoteCommandHandler|100%|83.3%|
|RoleRadar.Application.Notes.Commands.AppendToNoteCommandValidator|78.5%|62.5%|
|RoleRadar.Application.Notes.Commands.ArchiveNoteCommand|100%||
|RoleRadar.Application.Notes.Commands.ArchiveNoteCommandHandler|98.7%|85.7%|
|RoleRadar.Application.Notes.Commands.ArchiveNoteCommandValidator|100%|100%|
|RoleRadar.Application.Notes.Commands.CreateNoteCommand|100%||
|RoleRadar.Application.Notes.Commands.CreateNoteCommandHandler|98.1%|83.3%|
|RoleRadar.Application.Notes.Commands.CreateNoteCommandValidator|85.4%|82.1%|
|RoleRadar.Application.Notes.Commands.NoteArchivedDto|100%||
|RoleRadar.Application.Notes.Commands.NoteCreatedDto|100%||
|RoleRadar.Application.Notes.Commands.NoteSectionReplacedDto|100%||
|RoleRadar.Application.Notes.Commands.NoteUpdatedDto|100%||
|RoleRadar.Application.Notes.Commands.ProvenanceDto|100%||
|RoleRadar.Application.Notes.Commands.ReplaceNoteSectionCommand|100%||
|RoleRadar.Application.Notes.Commands.ReplaceNoteSectionCommandHandler|91.4%|81.2%|
|RoleRadar.Application.Notes.Commands.ReplaceNoteSectionCommandValidator|94.4%|91.6%|
|RoleRadar.Application.Notes.Queries.GetNoteQuery|100%||
|RoleRadar.Application.Notes.Queries.GetNoteQueryHandler|93%|94.1%|
|RoleRadar.Application.Notes.Queries.NoteDetailDto|100%||
|RoleRadar.Application.Notes.Queries.NoteSearchItemDto|100%||
|RoleRadar.Application.Notes.Queries.NoteSearchResponseDto|100%||
|RoleRadar.Application.Notes.Queries.SearchNotesQuery|100%||
|RoleRadar.Application.Notes.Queries.SearchNotesQueryHandler|95%|58.3%|
|RoleRadar.Application.Reading.ApplicationOrderingExtensions|50%|37.5%|
|RoleRadar.Application.Reading.CriterionOrderingExtensions|50%|50%|
|RoleRadar.Application.Reading.Models.ApplicationReadModel|94.4%|50%|
|RoleRadar.Application.Reading.Models.CompanyReadModel|100%||
|RoleRadar.Application.Reading.Models.CriterionReadModel|100%||
|RoleRadar.Application.Reading.Models.NoteReadModel|100%||
|RoleRadar.Application.Reading.Models.PostingReadModel|100%||
|RoleRadar.Application.Reading.Models.SearchRunReadModel|100%||
|RoleRadar.Application.Reading.PostingOrderingExtensions|62.5%|62.5%|
|RoleRadar.Application.Tracking.Commands.AddContactCommand|100%||
|RoleRadar.Application.Tracking.Commands.AddContactCommandHandler|86.6%|66.6%|
|RoleRadar.Application.Tracking.Commands.ApplicationEventInputDto|100%||
|RoleRadar.Application.Tracking.Commands.ApplicationRecordResultDto|100%||
|RoleRadar.Application.Tracking.Commands.CareerSourceInputDto|100%||
|RoleRadar.Application.Tracking.Commands.CompanyRecordResultDto|100%||
|RoleRadar.Application.Tracking.Commands.CompanyRecordSummaryDto|100%||
|RoleRadar.Application.Tracking.Commands.ContactRedactResultDto|100%||
|RoleRadar.Application.Tracking.Commands.ContactResultDto|100%||
|RoleRadar.Application.Tracking.Commands.PostingDto|100%||
|RoleRadar.Application.Tracking.Commands.PostingRecordResultDto|100%||
|RoleRadar.Application.Tracking.Commands.PostingTransitionResultDto|100%||
|RoleRadar.Application.Tracking.Commands.RecordApplicationCommand|100%||
|RoleRadar.Application.Tracking.Commands.RecordApplicationCommandHandler|90%|78.9%|
|RoleRadar.Application.Tracking.Commands.RecordApplicationCommandValidator|96.6%|93.7%|
|RoleRadar.Application.Tracking.Commands.RecordCompanyCommand|100%||
|RoleRadar.Application.Tracking.Commands.RecordCompanyCommandHandler|92.7%|76.1%|
|RoleRadar.Application.Tracking.Commands.RecordCompanyCommandValidator|96.1%|97.3%|
|RoleRadar.Application.Tracking.Commands.RecordPostingCommand|100%||
|RoleRadar.Application.Tracking.Commands.RecordPostingCommandHandler|87.4%|72.2%|
|RoleRadar.Application.Tracking.Commands.RecordPostingCommandValidator|75%|76.3%|
|RoleRadar.Application.Tracking.Commands.RedactContactCommand|100%||
|RoleRadar.Application.Tracking.Commands.RedactContactCommandHandler|81.8%|50%|
|RoleRadar.Application.Tracking.Commands.SignalInputDto|100%||
|RoleRadar.Application.Tracking.Commands.SignalInputDtoConverter|72.5%|64.7%|
|RoleRadar.Application.Tracking.Commands.TrackerLogCommand|100%||
|RoleRadar.Application.Tracking.Commands.TrackerLogCommandHandler|90.7%|67%|
|RoleRadar.Application.Tracking.Commands.TrackerLogResultDto|100%||
|RoleRadar.Application.Tracking.Commands.TransitionPostingCommand|100%||
|RoleRadar.Application.Tracking.Commands.TransitionPostingCommandHandler|95.3%|89.4%|
|RoleRadar.Application.Tracking.Commands.TransitionPostingCommandValidator|96.2%|96.1%|
|RoleRadar.Application.Tracking.Commands.TransitionWarningDto|100%||
|RoleRadar.Application.Tracking.Commands.TransitionWarnings|100%|100%|
|RoleRadar.Application.Tracking.Import.Commands.ImportTrackerCommand|100%||
|RoleRadar.Application.Tracking.Import.Commands.TrackerImportCoordinator|95.5%|81.2%|
|RoleRadar.Application.Tracking.Import.FileReconciliationSummary|91.6%|100%|
|RoleRadar.Application.Tracking.Import.Pipeline.AppendixRowsStep|92.6%|71.8%|
|RoleRadar.Application.Tracking.Import.Pipeline.ApplicationsStep|90.2%|73.1%|
|RoleRadar.Application.Tracking.Import.Pipeline.CompanyAliasesStep|78.2%|65%|
|RoleRadar.Application.Tracking.Import.Pipeline.CriteriaSeedingPipelineStep|90.9%|83.3%|
|RoleRadar.Application.Tracking.Import.Pipeline.CriteriaSeedingResult|100%||
|RoleRadar.Application.Tracking.Import.Pipeline.CriteriaSeedingStep|97.8%|73%|
|RoleRadar.Application.Tracking.Import.Pipeline.CsvHeaderValidator|94.1%|60%|
|RoleRadar.Application.Tracking.Import.Pipeline.PostingsJsonStep|88.8%|75%|
|RoleRadar.Application.Tracking.Import.Pipeline.TrackerImportContext|100%|66.6%|
|RoleRadar.Application.Tracking.Import.Pipeline.TrackerImportPipeline|94.8%|92.3%|
|RoleRadar.Application.Tracking.Import.Pipeline.TrackerImportPipelineFactory|100%|50%|
|RoleRadar.Application.Tracking.Import.Pipeline.WatchlistCompaniesStep|75.8%|62.1%|
|RoleRadar.Application.Tracking.Import.Report.TrackerImportReportGenerator|91%|67.8%|
|RoleRadar.Application.Tracking.Import.SourceFieldCatalog|100%|100%|
|RoleRadar.Application.Tracking.Import.SourceFieldDefinition|100%||
|RoleRadar.Application.Tracking.Import.TrackerImportFieldLimiter|94.8%|90%|
|RoleRadar.Application.Tracking.Import.TrackerImportRowError|100%||
|RoleRadar.Application.Tracking.Import.TrackerImportSummary|100%|75%|
|RoleRadar.Application.Tracking.Import.TrackerImportTextSanitizer|100%|100%|
|RoleRadar.Application.Tracking.Queries.ApplicationEventSummaryItemDto|100%||
|RoleRadar.Application.Tracking.Queries.ApplicationSummaryItemDto|100%||
|RoleRadar.Application.Tracking.Queries.CareerSourceSummaryItemDto|100%||
|RoleRadar.Application.Tracking.Queries.CompanySummaryItemDto|100%||
|RoleRadar.Application.Tracking.Queries.ContactDto|100%||
|RoleRadar.Application.Tracking.Queries.CriterionSummaryItemDto|100%||
|RoleRadar.Application.Tracking.Queries.DecidedPostingSummaryItemDto|66.6%||
|RoleRadar.Application.Tracking.Queries.GetContactQuery|100%||
|RoleRadar.Application.Tracking.Queries.GetContactQueryHandler|89.2%|50%|
|RoleRadar.Application.Tracking.Queries.PostingSummaryItemDto|100%||
|RoleRadar.Application.Tracking.Queries.TrackerQueryHandler|96.2%|67.9%|
|RoleRadar.Application.Tracking.Queries.TrackerQueryRequest|100%||
|RoleRadar.Application.Tracking.Queries.TrackerQueryResultDto|100%||

</details>
<details><summary>RoleRadar.Domain - 93.9%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**RoleRadar.Domain**|**93.9%**|**82.1%**|
|RoleRadar.Domain.Audit.ApplicationAuditSummary|100%||
|RoleRadar.Domain.Audit.AuditContentSummary|88%|60%|
|RoleRadar.Domain.Audit.AuditDetails|100%||
|RoleRadar.Domain.Audit.AuditLogEntry|88.6%|68.6%|
|RoleRadar.Domain.Audit.AuditOperation|100%|65.7%|
|RoleRadar.Domain.Audit.CompanyAuditSummary|100%||
|RoleRadar.Domain.Audit.ContactAuditContentSummary|100%||
|RoleRadar.Domain.Audit.CriterionAuditSummary|100%||
|RoleRadar.Domain.Audit.PostingAuditSummary|100%||
|RoleRadar.Domain.Common.OwnerId|85.7%|100%|
|RoleRadar.Domain.Common.RoleRadarStoreMetrics|100%|100%|
|RoleRadar.Domain.Common.Sha256Hasher|100%||
|RoleRadar.Domain.Common.SystemUtcClock|100%||
|RoleRadar.Domain.Notes.MarkdownImportParser|88.4%|82.3%|
|RoleRadar.Domain.Notes.MarkdownSectionParser|91.1%|82.8%|
|RoleRadar.Domain.Notes.Note|95.5%|67.5%|
|RoleRadar.Domain.Notes.NoteImportMetadata|88%|50%|
|RoleRadar.Domain.Notes.NoteRevision|88.8%|57.1%|
|RoleRadar.Domain.Notes.NoteStatusExtensions|100%|100%|
|RoleRadar.Domain.Notes.ParsedImportSection|90%||
|RoleRadar.Domain.Notes.ParsedMarkdownHeading|90%||
|RoleRadar.Domain.Notes.ProvenanceKindExtensions|100%|93.7%|
|RoleRadar.Domain.Notes.ProvenanceMarker|92.8%|75%|
|RoleRadar.Domain.Notes.SectionReplacementOutcome|100%||
|RoleRadar.Domain.Tracking.Application|97%|88.7%|
|RoleRadar.Domain.Tracking.ApplicationEvent|97.8%|87.5%|
|RoleRadar.Domain.Tracking.CareerSource|100%|89.1%|
|RoleRadar.Domain.Tracking.Company|100%|100%|
|RoleRadar.Domain.Tracking.Compensation|100%||
|RoleRadar.Domain.Tracking.Contact|93.4%|64.2%|
|RoleRadar.Domain.Tracking.Criterion|98.7%|90.9%|
|RoleRadar.Domain.Tracking.DateInferenceRule|100%|86.6%|
|RoleRadar.Domain.Tracking.DesiredSalary|100%||
|RoleRadar.Domain.Tracking.DispositionMatrix|100%|100%|
|RoleRadar.Domain.Tracking.GhostLikelihoodEvaluationResult|100%||
|RoleRadar.Domain.Tracking.GhostLikelihoodEvaluator|20%|0%|
|RoleRadar.Domain.Tracking.GhostLikelihoodRule|94.2%|88.8%|
|RoleRadar.Domain.Tracking.Posting|98.8%|86.4%|
|RoleRadar.Domain.Tracking.PostingCandidate|100%||
|RoleRadar.Domain.Tracking.PostingDifference|100%||
|RoleRadar.Domain.Tracking.PostingSignal|100%|100%|
|RoleRadar.Domain.Tracking.ResolvedDateOutcome|100%||
|RoleRadar.Domain.Tracking.SearchRun|91.8%|71.4%|
|RoleRadar.Domain.Tracking.TitleNormalizer|100%|100%|
|RoleRadar.Domain.Tracking.TrackingEnumExtensions|85.9%|81.8%|
|RoleRadar.Domain.Tracking.TrackingIndexValidationException|100%|50%|
|RoleRadar.Domain.Tracking.UrlNormalizer|97%|80.5%|

</details>
