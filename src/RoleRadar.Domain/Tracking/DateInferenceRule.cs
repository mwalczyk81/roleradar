using System.Globalization;
using System.Text.RegularExpressions;

namespace RoleRadar.Domain.Tracking;

public enum DateResolutionKind
{
    Exact,
    InferredSameYear,
    InferredRollback,
    DefaultedBlank,
    DefaultedImpossible
}

public sealed record ResolvedDateOutcome(
    DateOnly ResolvedDate,
    DateResolutionKind Kind,
    bool IsApproximate,
    bool IsFlagged,
    string? Note);

/// <summary>
/// Domain rules for parsing and inferring dates from legacy tracker inputs,
/// supporting month-day derivation with calendar rollback, approximation markers,
/// stated-date parentheticals, and graceful defaulting of impossible dates.
/// </summary>
public static partial class DateInferenceRule
{
    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex(@"^(?<month>\d{1,2})[/\-](?<day>\d{1,2})$")]
    private static partial Regex MonthDayRegex();

    public static ResolvedDateOutcome Resolve(string? rawInput, DateOnly defaultDate)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new ResolvedDateOutcome(
                ResolvedDate: defaultDate,
                Kind: DateResolutionKind.DefaultedBlank,
                IsApproximate: false,
                IsFlagged: false,
                Note: null);
        }

        var text = rawInput.Trim();
        var isApproximate = false;
        string? note = null;

        // Check for leading approximation marker "~"
        if (text.StartsWith('~'))
        {
            isApproximate = true;
            text = text[1..].Trim();
        }

        // Check for trailing parenthetical e.g. "(stated date, may differ from submit date)"
        var match = ParentheticalRegex().Match(text);
        if (match.Success)
        {
            note = match.Groups[1].Value.Trim();
            text = text.Remove(match.Index, match.Length).Trim();
        }

        // 1. Try full ISO date YYYY-MM-DD
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fullDate))
        {
            return new ResolvedDateOutcome(
                ResolvedDate: fullDate,
                Kind: DateResolutionKind.Exact,
                IsApproximate: isApproximate,
                IsFlagged: isApproximate || note is not null,
                Note: note);
        }

        // 2. Try month-day partial format M/D, MM/DD, M-D, MM-DD
        var mdMatch = MonthDayRegex().Match(text);
        if (mdMatch.Success)
        {
            var month = int.Parse(mdMatch.Groups["month"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(mdMatch.Groups["day"].Value, CultureInfo.InvariantCulture);

            if (month is < 1 or > 12)
            {
                return new ResolvedDateOutcome(
                    ResolvedDate: defaultDate,
                    Kind: DateResolutionKind.DefaultedImpossible,
                    IsApproximate: isApproximate,
                    IsFlagged: true,
                    Note: note);
            }

            var daysInTargetMonth = DateTime.DaysInMonth(defaultDate.Year, month);
            if (day < 1 || day > daysInTargetMonth)
            {
                return new ResolvedDateOutcome(
                    ResolvedDate: defaultDate,
                    Kind: DateResolutionKind.DefaultedImpossible,
                    IsApproximate: isApproximate,
                    IsFlagged: true,
                    Note: note);
            }

            var candidate = new DateOnly(defaultDate.Year, month, day);
            if (candidate > defaultDate)
            {
                // Rollback rule: derives date in previous calendar year
                var rollbackYear = defaultDate.Year - 1;
                var daysInRollbackMonth = DateTime.DaysInMonth(rollbackYear, month);
                var safeDay = Math.Min(day, daysInRollbackMonth);
                var rollbackDate = new DateOnly(rollbackYear, month, safeDay);

                return new ResolvedDateOutcome(
                    ResolvedDate: rollbackDate,
                    Kind: DateResolutionKind.InferredRollback,
                    IsApproximate: isApproximate,
                    IsFlagged: isApproximate || note is not null,
                    Note: note);
            }

            return new ResolvedDateOutcome(
                ResolvedDate: candidate,
                Kind: DateResolutionKind.InferredSameYear,
                IsApproximate: isApproximate,
                IsFlagged: isApproximate || note is not null,
                Note: note);
        }

        // Any other unrecognized format defaults to defaultDate and is flagged
        return new ResolvedDateOutcome(
            ResolvedDate: defaultDate,
            Kind: DateResolutionKind.DefaultedImpossible,
            IsApproximate: isApproximate,
            IsFlagged: true,
            Note: note);
    }
}
