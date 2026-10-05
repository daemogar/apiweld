using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ApiWeld.Generator;

/// <summary>The query and header names that make an operation paged.</summary>
/// <param name="Offset">The query parameter holding the first row's offset.</param>
/// <param name="Limit">The query parameter holding the page size.</param>
/// <param name="TotalHeader">The response header holding the total row count.</param>
public sealed record PagingConvention(string Offset = "offset", string Limit = "limit", string TotalHeader = "X-Total-Count");

/// <summary>A manifest that cannot be used, with the reason.</summary>
public sealed class ManifestException(string message) : Exception(message);

/// <summary>What <c>apiweld generate</c> reads: which descriptions to generate from, and how.</summary>
/// <remarks>See README.md, "The manifest".</remarks>
public sealed partial record Manifest
{
	/// <summary>File paths or single-folder wildcards, relative to the manifest.</summary>
	public required IReadOnlyList<string> Descriptions { get; init; }

	/// <summary>The namespace generated code is written in.</summary>
	public required string Namespace { get; init; }

	/// <summary>The consumer's root client class, whose generated half is written.</summary>
	public required string Client { get; init; }

	/// <summary>Where generated files go, relative to the manifest.</summary>
	public string Output { get; init; } = "Generated";

	/// <summary>Leading path segments left out of navigation, such as <c>/api</c>.</summary>
	public IReadOnlyList<string> BasePaths { get; init; } = [];

	/// <summary>The names that make an operation paged.</summary>
	public PagingConvention Paging { get; init; } = new();

	/// <summary>Base-name overrides, keyed by description file stem or component schema name.</summary>
	public IReadOnlyDictionary<string, string> Names { get; init; } = new Dictionary<string, string>();

	/// <summary>Singulars for plurals the built-in rules get wrong, keyed by the lowercase plural.</summary>
	public IReadOnlyDictionary<string, string> Singulars { get; init; } = new Dictionary<string, string>();

	[GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
	private static partial Regex IdentifierPattern();

	[GeneratedRegex("^[A-Za-z]+$")]
	private static partial Regex WordPattern();

	static readonly string[] Keys = ["descriptions", "namespace", "client", "output", "basePaths", "paging", "names", "singulars"];

	static readonly string[] PagingKeys = ["offset", "limit", "totalHeader"];

	static bool IsIdentifier(string text) => IdentifierPattern().IsMatch(text) && !Words.IsKeyword(text);

	/// <summary>Reads a manifest; throws <see cref="ManifestException"/> naming the first problem.</summary>
	public static Manifest Parse(string json)
	{
		JsonNode? parsed;

		try
		{
			parsed = JsonNode.Parse(json);
		}
		catch (JsonException exception)
		{
			throw new ManifestException($"not valid JSON — {exception.Message}");
		}

		if (parsed is not JsonObject root)
			throw new ManifestException("the manifest must be a JSON object.");

		if (root.Select(pair => pair.Key).FirstOrDefault(key => !key.StartsWith('$') && !Keys.Contains(key)) is { } unknown)
			throw new ManifestException($"unknown key \"{unknown}\".");

		var descriptions = Strings(root, "descriptions") ?? throw Missing("descriptions");

		if (descriptions.Count == 0)
			throw new ManifestException("\"descriptions\" lists no files.");

		var @namespace = Text(root, "namespace") ?? throw Missing("namespace");

		if (!@namespace.Split('.').All(IsIdentifier))
			throw new ManifestException($"\"namespace\" is not a valid C# namespace: {@namespace}");

		var client = Text(root, "client") ?? throw Missing("client");

		if (!IsIdentifier(client))
			throw new ManifestException($"\"client\" is not a valid C# identifier: {client}");

		var names = new Dictionary<string, string>(StringComparer.Ordinal);

		if (Section(root, "names") is { } overrides)
			foreach (var (key, value) in overrides)
				names[key] = value is JsonValue text && text.TryGetValue<string>(out var name) && IsIdentifier(name)
					? name
					: throw new ManifestException($"\"names\".\"{key}\" must be a valid C# identifier.");

		var singulars = new Dictionary<string, string>(StringComparer.Ordinal);

		if (Section(root, "singulars") is { } plurals)
			foreach (var (plural, value) in plurals)
				singulars[plural.ToLowerInvariant()] = WordPattern().IsMatch(plural) && value is JsonValue word
					&& word.TryGetValue<string>(out var singular) && WordPattern().IsMatch(singular)
						? singular
						: throw new ManifestException($"\"singulars\".\"{plural}\" must map one word to one word.");

		var paging = Section(root, "paging");

		if (paging?.Select(pair => pair.Key).FirstOrDefault(key => !PagingKeys.Contains(key)) is { } unknownPaging)
			throw new ManifestException($"unknown key \"paging\".\"{unknownPaging}\".");
		var defaults = new PagingConvention();

		return new()
		{
			Descriptions = descriptions,
			Namespace = @namespace,
			Client = client,
			Output = Text(root, "output") ?? "Generated",
			BasePaths = [.. (Strings(root, "basePaths") ?? []).Select(path => "/" + path.Trim('/'))],
			Paging = new(
				Text(paging, "offset") ?? defaults.Offset,
				Text(paging, "limit") ?? defaults.Limit,
				Text(paging, "totalHeader") ?? defaults.TotalHeader),
			Names = names,
			Singulars = singulars
		};
	}

	static ManifestException Missing(string key) => new($"\"{key}\" is required.");

	static JsonObject? Section(JsonObject node, string key) => node[key] switch
	{
		null => null,
		JsonObject section => section,
		_ => throw new ManifestException($"\"{key}\" must be an object.")
	};

	static string? Text(JsonObject? node, string key) => node?[key] switch
	{
		null => null,
		JsonValue value when value.TryGetValue<string>(out var text) => text.Trim() is { Length: > 0 } trimmed ? trimmed : null,
		_ => throw new ManifestException($"\"{key}\" must be a string.")
	};

	static List<string>? Strings(JsonObject node, string key) => node[key] switch
	{
		null => null,
		JsonArray array => [.. array.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text)
			? text
			: throw new ManifestException($"\"{key}\" must list strings."))],
		_ => throw new ManifestException($"\"{key}\" must be an array of strings.")
	};
}
