using System.Globalization;
using System.Text.RegularExpressions;

namespace ApiWeld.Http;

/// <summary>Parses the date and time forms the tolerant converters accept: ISO 8601 extended or basic, and slash dates.</summary>
/// <remarks>See README.md, "Tolerant JSON".</remarks>
static partial class DateText
{
	// yyyy-MM-dd, yyyyMMdd, yyyy/M/d or M/d/yyyy, then optionally a time after T or a space.
	[GeneratedRegex(@"^(?:(?<y>[0-9]{4})-(?<m>[0-9]{2})-(?<d>[0-9]{2})|(?<y>[0-9]{4})(?<m>[0-9]{2})(?<d>[0-9]{2})|(?<y>[0-9]{4})/(?<m>[0-9]{1,2})/(?<d>[0-9]{1,2})|(?<m>[0-9]{1,2})/(?<d>[0-9]{1,2})/(?<y>[0-9]{4}))(?:[Tt ](?<time>.+))?$", RegexOptions.CultureInvariant)]
	private static partial Regex Date();

	// H:mm[:ss] or HHmm[ss], a fraction of a second after a point or comma, then an optional offset.
	[GeneratedRegex(@"^(?:(?<h>[0-9]{1,2}):(?<mi>[0-9]{2})(?::(?<s>[0-9]{2})(?:[.,](?<f>[0-9]+))?)?|(?<h>[0-9]{2})(?<mi>[0-9]{2})(?:(?<s>[0-9]{2})(?:[.,](?<f>[0-9]+))?)?)(?<zone>[Zz]|(?<sign>[+-])(?<zh>[0-9]{2})(?::?(?<zm>[0-9]{2}))?)?$", RegexOptions.CultureInvariant)]
	private static partial Regex Time();

	/// <summary>A date-time; one with no offset, or a bare date, is taken as UTC.</summary>
	public static bool TryParseDateTime(string text, out DateTimeOffset value)
	{
		value = default;

		if (!TryParse(text, out var day, out var clock, out var offset))
			return false;

		offset ??= TimeSpan.Zero;

		if (offset.Value.Duration() > TimeSpan.FromHours(14))
			return false;

		value = new DateTimeOffset(day.ToDateTime(clock ?? TimeOnly.MinValue), offset.Value);

		return true;
	}

	/// <summary>A date, or the date part of a date-time as written.</summary>
	public static bool TryParseDate(string text, out DateOnly value) => TryParse(text, out value, out _, out _);

	/// <summary>A time of day; an offset is accepted and the clock time kept as written.</summary>
	public static bool TryParseTime(string text, out TimeOnly value) => TryParseClock(text, out value, out _);

	static bool TryParse(string text, out DateOnly day, out TimeOnly? clock, out TimeSpan? offset)
	{
		day = default;
		clock = null;
		offset = null;

		var match = Date().Match(text);

		if (!match.Success)
			return false;

		if (!int.TryParse(match.Groups["y"].ValueSpan, CultureInfo.InvariantCulture, out var year)
			|| !int.TryParse(match.Groups["m"].ValueSpan, CultureInfo.InvariantCulture, out var month)
			|| !int.TryParse(match.Groups["d"].ValueSpan, CultureInfo.InvariantCulture, out var date)
			|| month is < 1 or > 12 || year < 1 || date < 1 || date > DateTime.DaysInMonth(year, month))
			return false;

		day = new DateOnly(year, month, date);

		if (match.Groups["time"] is not { Success: true } time)
			return true;

		var parsed = TryParseClock(time.Value, out var at, out offset);
		clock = at;

		return parsed;
	}

	static bool TryParseClock(string text, out TimeOnly value, out TimeSpan? offset)
	{
		value = default;
		offset = null;

		var match = Time().Match(text);

		if (!match.Success)
			return false;

		var hour = int.Parse(match.Groups["h"].ValueSpan, CultureInfo.InvariantCulture);
		var minute = int.Parse(match.Groups["mi"].ValueSpan, CultureInfo.InvariantCulture);
		var second = match.Groups["s"].Success ? int.Parse(match.Groups["s"].ValueSpan, CultureInfo.InvariantCulture) : 0;

		if (hour > 23 || minute > 59 || second > 59)
			return false;

		// Digits past the seventh, finer than a tick, are dropped rather than rounded, so a time never rolls past midnight.
		var fraction = match.Groups["f"].Value;
		var ticks = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(7, '0').AsSpan(0, 7), CultureInfo.InvariantCulture);

		value = new TimeOnly(hour, minute, second).Add(TimeSpan.FromTicks(ticks));

		if (match.Groups["zone"].Success)
		{
			var hours = match.Groups["zh"].Success ? int.Parse(match.Groups["zh"].ValueSpan, CultureInfo.InvariantCulture) : 0;
			var minutes = match.Groups["zm"].Success ? int.Parse(match.Groups["zm"].ValueSpan, CultureInfo.InvariantCulture) : 0;

			if (minutes > 59)
				return false;

			var size = new TimeSpan(hours, minutes, 0);
			offset = match.Groups["sign"].Value == "-" ? -size : size;
		}

		return true;
	}
}
