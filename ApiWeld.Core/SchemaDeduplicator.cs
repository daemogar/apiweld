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

		Dictionary<string, Use> uses = [];

		foreach (var (name, value) in document)
			if (name is not "components")
				Walk(value, Use.None, inSchema: false, document, uses, []);

		// Only what a path or another schema reaches: an orphan is not generated, so
		// folding one in would rename a live type after a dead one.
		var names = schemas.Select(p => p.Key).Where(referenced.Contains).ToArray();

		Dictionary<string, string> canonical = [];

		foreach (var name in names)
		{
			if (canonical.ContainsKey(name))
				continue;

			// A request and a response are never one type, however alike they look.
			var group = names
				.Where(p => !canonical.ContainsKey(p)
					&& uses.GetValueOrDefault(p) == uses.GetValueOrDefault(name)
					&& JsonNode.DeepEquals(schemas[p], schemas[name]))
				.ToArray();

			var winner = Array.Find(group, p => p == $"{resourceName}_get_response") ?? group[0];

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

	/// <summary>Which directions reach a schema.</summary>
	[Flags]
	enum Use { None = 0, Request = 1, Response = 2 }

	/// <summary>Records the direction each schema is reached in, following references into components.</summary>
	static void Walk(JsonNode? node, Use use, bool inSchema, JsonObject document, Dictionary<string, Use> uses, HashSet<(string, Use)> seen)
	{
		if (node is JsonArray array)
		{
			foreach (var item in array)
				Walk(item, use, inSchema, document, uses, seen);

			return;
		}

		if (node is not JsonObject source)
			return;

		foreach (var (name, value) in source)
		{
			if (name is "$ref" && value is JsonValue reference && reference.TryGetValue<string>(out var target)
				&& target.StartsWith("#/components/", StringComparison.Ordinal))
			{
				if (Target(value) is string schema)
					uses[schema] = uses.GetValueOrDefault(schema) | use;

				if (seen.Add((target, use)))
					Walk(Resolve(document, target), use, inSchema || Target(value) is not null, document, uses, seen);

				continue;
			}

			// Inside a schema these are property names, not the operation's keywords.
			var next = inSchema ? use : name switch
			{
				"requestBody" => Use.Request,
				"responses" => Use.Response,
				_ => use,
			};

			Walk(value, next, inSchema || name is "schema", document, uses, seen);
		}
	}

	static JsonNode? Resolve(JsonObject document, string target)
	{
		JsonNode? node = document;

		foreach (var segment in target[2..].Split('/'))
			node = node is JsonObject current ? current[segment.Replace("~1", "/").Replace("~0", "~")] : null;

		return node;
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
