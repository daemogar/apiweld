using System.Text.Json.Nodes;

using ApiWeld.Http;

namespace ApiWeld.Generator.Model;

/// <summary>Chooses which of a body's declared media types the client pins.</summary>
internal static class MediaTypes
{
	/// <summary>The most specific versioned JSON media type, else plain JSON, else null when nothing is JSON.</summary>
	public static (string MediaType, JsonNode? Schema)? Pick(JsonNode? content)
	{
		if (content is not JsonObject map)
			return null;

		var json = map.Where(entry => IsJson(entry.Key)).ToList();

		if (json.Count == 0)
			return null;

		var versioned = json
			.Select(entry => (Entry: entry, Version: MediaTypeVersion.Read(entry.Key)))
			.Where(pair => pair.Version is not null)
			.OrderByDescending(pair => pair.Version!.Split('.').Length)
			.ThenByDescending(pair => Numeric(pair.Version!))
			.ThenBy(pair => pair.Entry.Key, StringComparer.Ordinal)
			.Select(pair => pair.Entry)
			.ToList();

		var chosen = versioned.Count > 0
			? versioned[0]
			: json.OrderBy(entry => entry.Key == "application/json" ? 0 : 1).ThenBy(entry => entry.Key, StringComparer.Ordinal).First();

		return (chosen.Key, (chosen.Value as JsonObject)?["schema"]);
	}

	static bool IsJson(string mediaType)
	{
		var essence = mediaType.Split(';')[0].Trim().ToLowerInvariant();

		return essence is "application/json" or "*/*" || essence.EndsWith("+json", StringComparison.Ordinal);
	}

	static Version Numeric(string version)
		=> System.Version.TryParse(version.Contains('.') ? version : version + ".0", out var parsed) ? parsed : new Version(0, 0);
}
