using System.Globalization;
using System.Text.RegularExpressions;

namespace ApiWeld.Http;

/// <summary>Reads and normalizes the version token a vendor media type carries.</summary>
/// <remarks>See README.md, "Media-type versions".</remarks>
public static partial class MediaTypeVersion
{
	[GeneratedRegex(@"(?:^|[./-])v(\d+(?:\.\d+)*)(?=\+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex Token();

	/// <summary>The normalized version a media type carries, or null when it carries none.</summary>
	public static string? Read(string? mediaType)
	{
		if (string.IsNullOrWhiteSpace(mediaType))
			return null;

		var essence = mediaType.Split(';', 2)[0].Trim();
		var match = Token().Match(essence);

		return match.Success ? Normalize(match.Groups[1].Value) : null;
	}

	/// <summary>Drops leading zeros and trailing zero parts: <c>15.0.0</c> becomes <c>15</c>.</summary>
	public static string Normalize(string version)
	{
		var parts = version.Trim().TrimStart('v', 'V')
			.Split('.')
			.Select(part => part.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0")
			.ToList();

		while (parts.Count > 1 && parts[^1] == "0")
			parts.RemoveAt(parts.Count - 1);

		return string.Join('.', parts);
	}

	/// <summary>The member name for a normalized version: <c>12.6</c> becomes <c>V12_6</c>, null becomes <c>V0</c>.</summary>
	public static string MemberName(string? version) => "V" + (version ?? "0").Replace('.', '_');

	/// <summary>Whether <paramref name="candidate"/> is later than <paramref name="baseline"/> and shares its major: <c>11.4</c> against <c>11.1</c>.</summary>
	public static bool IsNewerInMajor(string candidate, string baseline)
	{
		if (Parts(candidate) is not { } newer || Parts(baseline) is not { } older || newer[0] != older[0])
			return false;

		for (var i = 1; i < Math.Max(newer.Count, older.Count); i++)
		{
			var (a, b) = (i < newer.Count ? newer[i] : 0, i < older.Count ? older[i] : 0);

			if (a != b)
				return a > b;
		}

		return false;
	}

	/// <summary>The media type with its version token set to <paramref name="version"/>, padded to the token's number of parts.</summary>
	public static string WithVersion(string mediaType, string version)
	{
		var split = mediaType.Split(';', 2);
		var match = Token().Match(split[0]);

		if (!match.Success)
			return mediaType;

		var token = match.Groups[1];
		var parts = Normalize(version).Split('.').ToList();

		while (parts.Count < token.Value.Split('.').Length)
			parts.Add("0");

		var essence = split[0][..token.Index] + string.Join('.', parts) + split[0][(token.Index + token.Length)..];

		return split.Length == 2 ? essence + ";" + split[1] : essence;
	}

	static List<long>? Parts(string version)
	{
		var parts = new List<long>();

		foreach (var part in Normalize(version).Split('.'))
		{
			if (!long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
				return null;

			parts.Add(number);
		}

		return parts;
	}
}
