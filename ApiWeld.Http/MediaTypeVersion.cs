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
}
