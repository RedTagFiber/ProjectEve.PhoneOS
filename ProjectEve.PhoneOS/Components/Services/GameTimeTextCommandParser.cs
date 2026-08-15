using System.Globalization;
using System.Text.RegularExpressions;
using ProjectEve.Core.Time;

namespace ProjectEve.PhoneOS.Services;

public sealed class GameTimeTextCommandParser
{
    private static readonly Regex WaitAmount = new(
        @"\b(?:wait|skip|advance)\s+(?<n>\d+(?:\.\d+)?)\s*(?<unit>minutes?|mins?|hours?|hrs?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UntilTime = new(
        @"\b(?:wait\s+until|until|sleep\s+until|wake\s+(?:me\s+)?(?:up\s+)?(?:at\s+)?)\s*(?<time>\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<GameTimeAdvanceResult> ExecuteAsync(
        IWorldAdvanceCoordinator worldTime,
        string playerId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var raw = (text ?? "").Trim();
        var normalized = raw.ToLowerInvariant();

        if (normalized == "next event" ||
            normalized == "next" ||
            normalized == "wait until something happens" ||
            normalized.Contains("contacts me") ||
            normalized.Contains("contact me"))
            return await worldTime.AdvanceToNextPlayerEventAsync(playerId, cancellationToken);

        if (normalized == "next day" ||
            normalized == "tomorrow" ||
            normalized.Contains("skip a day"))
        {
            return await worldTime.AdvanceByAsync(
                playerId,
                TimeSpan.FromDays(1),
                "text_next_day",
                cancellationToken);
        }

        if (normalized.Contains("next morning") ||
            normalized.Contains("tomorrow morning") ||
            normalized == "sleep" ||
            normalized.Contains("go to bed"))
        {
            return await worldTime.AdvanceUntilAsync(
                playerId,
                NextLocalTime(worldTime.Now, 7, 0, forceTomorrow: true),
                "text_next_morning",
                cancellationToken);
        }

        var amount = WaitAmount.Match(raw);
        if (amount.Success &&
            double.TryParse(amount.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            var unit = amount.Groups["unit"].Value.ToLowerInvariant();
            var span = unit.StartsWith("h")
                ? TimeSpan.FromHours(value)
                : TimeSpan.FromMinutes(value);

            return await worldTime.AdvanceByAsync(
                playerId,
                span,
                "text_wait",
                cancellationToken);
        }

        var until = UntilTime.Match(raw);
        if (until.Success && TryParseClock(until.Groups["time"].Value, out var hour, out var minute))
        {
            return await worldTime.AdvanceUntilAsync(
                playerId,
                NextLocalTime(worldTime.Now, hour, minute, forceTomorrow: false),
                "text_wait_until",
                cancellationToken);
        }

        return new GameTimeAdvanceResult
        {
            FromGameTime = worldTime.Now,
            ToGameTime = worldTime.Now,
            Message = "Try: next event, wait 2 hours, wait 15 minutes, next morning, or wait until 7 PM."
        };
    }

    public async Task<GameTimeAdvanceResult> ExecuteAsync(
        IGameTimeService clock,
        string playerId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var raw = (text ?? "").Trim();
        var normalized = raw.ToLowerInvariant();

        if (normalized == "next event" ||
            normalized == "next" ||
            normalized == "wait until something happens" ||
            normalized.Contains("contacts me") ||
            normalized.Contains("contact me"))
            return await clock.AdvanceToNextPlayerEventAsync(playerId, cancellationToken);

        if (normalized == "next day" ||
            normalized == "tomorrow" ||
            normalized.Contains("skip a day"))
        {
            return await clock.AdvanceByAsync(
                playerId,
                TimeSpan.FromDays(1),
                "text_next_day",
                cancellationToken);
        }

        if (normalized.Contains("next morning") ||
            normalized.Contains("tomorrow morning") ||
            normalized == "sleep" ||
            normalized.Contains("go to bed"))
        {
            return await clock.AdvanceUntilAsync(
                playerId,
                NextLocalTime(clock.Now, 7, 0, forceTomorrow: true),
                "text_next_morning",
                cancellationToken);
        }

        var amount = WaitAmount.Match(raw);
        if (amount.Success &&
            double.TryParse(amount.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            var unit = amount.Groups["unit"].Value.ToLowerInvariant();
            var span = unit.StartsWith("h")
                ? TimeSpan.FromHours(value)
                : TimeSpan.FromMinutes(value);

            return await clock.AdvanceByAsync(
                playerId,
                span,
                "text_wait",
                cancellationToken);
        }

        var until = UntilTime.Match(raw);
        if (until.Success && TryParseClock(until.Groups["time"].Value, out var hour, out var minute))
        {
            return await clock.AdvanceUntilAsync(
                playerId,
                NextLocalTime(clock.Now, hour, minute, forceTomorrow: false),
                "text_wait_until",
                cancellationToken);
        }

        return new GameTimeAdvanceResult
        {
            FromGameTime = clock.Now,
            ToGameTime = clock.Now,
            Message = "Try: next event, wait 2 hours, wait 15 minutes, next morning, or wait until 7 PM."
        };
    }

    public static DateTimeOffset NextLocalTime(
        DateTimeOffset now,
        int hour,
        int minute,
        bool forceTomorrow)
    {
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);

        var candidate = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            hour,
            minute,
            0,
            now.Offset);

        if (forceTomorrow || candidate <= now)
            candidate = candidate.AddDays(1);

        return candidate;
    }

    private static bool TryParseClock(string text, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        var raw = (text ?? "").Trim().ToUpperInvariant().Replace(" ", "");

        var am = raw.EndsWith("AM", StringComparison.Ordinal);
        var pm = raw.EndsWith("PM", StringComparison.Ordinal);
        if (am || pm)
            raw = raw[..^2];

        var parts = raw.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out hour))
            return false;

        if (parts.Length == 2 && !int.TryParse(parts[1], out minute))
            return false;

        if (am || pm)
        {
            if (hour is < 1 or > 12) return false;
            if (hour == 12) hour = 0;
            if (pm) hour += 12;
        }

        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
    }
}
