using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

internal static class StatementImportTimePolicy
{
    private const int MinutesPerDay = 24 * 60;

    public static bool TryResolveBookedAtUtc(
        StatementImportRow row,
        TimeZoneInfo timeZone,
        out DateTime bookedAtUtc)
    {
        bookedAtUtc = default;
        if (row.TimestampPrecision == StatementImportTimestampPrecisions.Instant
            && row.EffectiveAtUtc is { } effectiveAtUtc
            && effectiveAtUtc.Kind == DateTimeKind.Utc
            && row.EffectiveDate is null)
        {
            bookedAtUtc = effectiveAtUtc;
            return true;
        }

        return row.TimestampPrecision == StatementImportTimestampPrecisions.Date
            && row.EffectiveDate is { } effectiveDate
            && row.EffectiveAtUtc is null
            && TryResolveFirstUtcInstant(effectiveDate, timeZone, out bookedAtUtc);
    }

    private static bool TryResolveFirstUtcInstant(
        DateOnly effectiveDate,
        TimeZoneInfo timeZone,
        out DateTime utc)
    {
        var local = DateTime.SpecifyKind(
            effectiveDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        for (var minute = 0; minute < MinutesPerDay; minute++)
        {
            var candidate = local.AddMinutes(minute);
            if (timeZone.IsInvalidTime(candidate))
            {
                continue;
            }

            if (timeZone.IsAmbiguousTime(candidate))
            {
                var earliestOffset = timeZone.GetAmbiguousTimeOffsets(candidate).Max();
                utc = new DateTimeOffset(candidate, earliestOffset).UtcDateTime;
                return true;
            }

            utc = TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);
            return true;
        }

        utc = default;
        return false;
    }
}
