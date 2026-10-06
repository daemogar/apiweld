using System.Text.Json.Nodes;

namespace ApiWeld.Core;

/// <summary>One component schema in place of every identical copy of it.</summary>
/// <remarks>Why a generator needs this, and why only referenced schemas participate: see README.md, "Deduplication rules".</remarks>
internal static class SchemaDeduplicator
{
	const string Reference = "#/components/schemas/";

	/// <summary>The document with identical referenced schemas folded onto one canonical name.</summary>
	public static JsonObject Deduplicate(JsonObject document, string resourceName)
	{
		if (document["components"]?["schemas"] is not JsonObject schemas)
			return document;

		HashSet<string> referenced = [];
		Collect(document, referenced);

		// Only what a path or another schema reaches: an orphan is not generated, so
		// folding one in would rename a live type after a dead one.
		var names = schemas.Select(p => p.Key).Where(referenced.Contains).ToArray();

		Dictionary<string, string> canonical = [];

		foreach (var name in names)
		{
			if (canonical.ContainsKey(name))
				continue;

			var group = names
				.Where(p => !canonical.ContainsKey(p)
					&& JsonNode.DeepEquals(schemas[p], schemas[name]))
				.ToArray();

			// A response wins over a request, and the get response over any other,
			// because a response is the type callers hold and map.
			var winner = Array.Find(group, p => p == $"{resourceName}_get_response")
				?? Array.Find(group, p => p.EndsWith("_response", StringComparison.Ordinal))
				?? group[0];

			foreach (var member in group)
				canonical[member] = winner;
		}

		var rewritten = (JsonObject)Rewrite(document, canonical)!;

		if (rewritten["components"]?["schemas"] is JsonObject remaining)
			foreach (var (name, winner) in canonical)
				if (name != winner)
					remaining.Remove(name);

		return rewritten;
	}

	static void Collect(JsonNode? node, HashSet<string> into)
	{
		if (node is JsonArray array)
		{
			foreach (var item in array)
				Collect(item, into);

			return;
		}

		if (node is not JsonObject source)
			return;

		foreach (var (name, value) in source)
			if (name is "$ref" && Target(value) is string target)
				into.Add(target);
			else
				Collect(value, into);
	}

	static JsonNode? Rewrite(JsonNode? node, Dictionary<string, string> canonical)
	{
		if (node is JsonArray array)
			return new JsonArray([.. array.Select(p => Rewrite(p, canonical))]);

		if (node is not JsonObject source)
			return node?.DeepClone();

		JsonObject result = [];

		foreach (var (name, value) in source)
			result[name] = name is "$ref"
				&& Target(value) is string target
				&& canonical.TryGetValue(target, out var winner)
					? JsonValue.Create($"{Reference}{winner}")
					: Rewrite(value, canonical);

		return result;
	}

	static string? Target(JsonNode? node)
		=> node is JsonValue value
			&& value.TryGetValue<string>(out var reference)
			&& reference.StartsWith(Reference, StringComparison.Ordinal)
				? reference[Reference.Length..]
				: null;
}
