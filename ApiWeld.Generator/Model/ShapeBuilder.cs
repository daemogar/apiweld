using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiWeld.Generator.Model;

/// <summary>Turns schemas into types, merging identical shapes that travel in the same direction.</summary>
internal sealed class ShapeBuilder(DiagnosticBag diagnostics, IReadOnlyDictionary<string, string> names)
{
	readonly Dictionary<string, ModelType> models = new(StringComparer.Ordinal);
	readonly Stack<string> resolving = new();
	readonly HashSet<JsonObject> active = new(ReferenceEqualityComparer.Instance);

	public IEnumerable<ModelType> Models => models.Values;

	public static ScalarRef? Scalar(string? type, string? format) => type switch
	{
		"string" => format switch
		{
			"uuid" => ScalarRef.Guid,
			"date-time" => ScalarRef.DateTimeOffset,
			"date" => ScalarRef.DateOnly,
			_ => ScalarRef.String
		},
		"integer" => format == "int64" ? ScalarRef.Long : ScalarRef.Int,
		"number" => ScalarRef.Decimal,
		"boolean" => ScalarRef.Bool,
		_ => null
	};

	public TypeRef Build(JsonNode? schema, JsonObject document, NameContext context)
	{
		var (node, component) = References.Resolve(schema, document);

		if (component is not null && resolving.Contains(component))
		{
			diagnostics.Warn($"{context.Source}: schema {component} refers to itself; the recursive property is typed as JsonElement.");
			return ScalarRef.Json;
		}

		if (node is not null && active.Contains(node))
		{
			diagnostics.Warn($"{context.Source}: {context.Candidate().Plain} refers to itself; the recursive property is typed as JsonElement.");
			return ScalarRef.Json;
		}

		if (component is not null)
		{
			if (names.TryGetValue(component, out var renamed))
				context = context.Rebase(renamed);
			else if (context.ErrorRoot)
				context = context.Rebase(Words.Pascal(component));

			resolving.Push(component);
		}

		if (node is not null)
			active.Add(node);

		try
		{
			return Resolved(node, document, context);
		}
		finally
		{
			if (node is not null)
				active.Remove(node);

			if (component is not null)
				resolving.Pop();
		}
	}

	TypeRef Resolved(JsonObject? node, JsonObject document, NameContext context)
	{
		if (node is null)
			return ScalarRef.Json;

		var type = JsonText.String(node, "type");

		if (node["enum"] is JsonArray values && (type is null or "string")
			&& values.All(value => value is null || (value is JsonValue text && text.TryGetValue<string>(out _))))
			return Enum(node, values, context);

		if (type == "array")
			return new ListRef(Build(node["items"], document, context.Item()));

		if (Scalar(type, JsonText.String(node, "format")) is { } scalar)
			return scalar;

		if (type is "object" or null && node["properties"] is JsonObject { Count: > 0 } properties)
			return Object(node, properties, document, context);

		if (type is "object" or null && node["additionalProperties"] is JsonObject additional)
			return new MapRef(Build(additional, document, context.Child("Value")));

		if (type is "object" or null && JsonText.Flag(node, "additionalProperties"))
			return new MapRef(ScalarRef.Json);

		if (node["allOf"] is not null)
			diagnostics.Warn($"{context.Source}: allOf is not merged; {context.Candidate().Plain} is typed as JsonElement.");

		return ScalarRef.Json;
	}

	TypeRef Object(JsonObject node, JsonObject properties, JsonObject document, NameContext context)
	{
		var required = (node["required"] as JsonArray)?
			.Select(item => (item as JsonValue)?.TryGetValue<string>(out var name) == true ? name : null)
			.OfType<string>()
			.ToHashSet(StringComparer.Ordinal) ?? [];

		var built = properties
			.Select(property => new ModelProperty(
				property.Key,
				Build(property.Value, document, context.Child(Words.Pascal(property.Key))),
				Description(property.Value, document),
				required.Contains(property.Key)))
			.ToList();

		var key = "{" + string.Join(",", built.OrderBy(p => p.JsonName, StringComparer.Ordinal).Select(p => Quote(p.JsonName) + ":" + Key(p.Type))) + "}";

		return new ModelRef(Intern(key, ModelKind.Object, context, model =>
		{
			model.Properties.AddRange(built);
			model.Summary = JsonText.String(node, "description");
		}));
	}

	TypeRef Enum(JsonObject node, JsonArray values, NameContext context)
	{
		var list = values
			.Select(value => (value as JsonValue)?.TryGetValue<string>(out var text) == true ? text : null)
			.OfType<string>()
			.Distinct(StringComparer.Ordinal)
			.ToList();

		if (list.Count == 0)
			return ScalarRef.String;

		return new ModelRef(Intern("enum[" + string.Join(",", list.Order(StringComparer.Ordinal).Select(Quote)) + "]",ModelKind.Enum, context, model =>
		{
			model.EnumValues.AddRange(list);
			model.Summary = JsonText.String(node, "description");
		}));
	}

	ModelType Intern(string shapeKey, ModelKind kind, NameContext context, Action<ModelType> fill)
	{
		var id = context.Direction + ":" + shapeKey;

		if (!models.TryGetValue(id, out var model))
		{
			model = new ModelType(shapeKey, context.Direction, kind);
			fill(model);
			models.Add(id, model);
		}

		var candidate = context.Candidate();

		if (!model.Candidates.Contains(candidate))
			model.Candidates.Add(candidate);

		return model;
	}

	static string? Description(JsonNode? schema, JsonObject document)
		=> JsonText.String(schema, "description") ?? JsonText.String(References.Resolve(schema, document).Node, "description");

	static string Quote(string text) => "\"" + JsonEncodedText.Encode(text).Value + "\"";

	static string Key(TypeRef type) => type switch
	{
		ScalarRef scalar => scalar.Name,
		ListRef list => "[" + Key(list.Item) + "]",
		MapRef map => "map<" + Key(map.Value) + ">",
		ModelRef model => model.Model.ShapeKey,
		_ => throw new InvalidOperationException($"Unknown type {type}.")
	};
}
