using ApiWeld.Generator.Model;

namespace ApiWeld.Generator.Emit;

/// <summary>Writes one model: a partial class for an object, an extensible enum for a string enum.</summary>
internal static class ModelEmitter
{
	/// <summary>Members every model inherits; a property of the same name would hide one.</summary>
	static readonly string[] Inherited = ["Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone", "ReferenceEquals", "Finalize"];

	public static GeneratedFile Emit(ModelType model, string @namespace)
		=> new($"Models/{model.Name}.g.cs", model.Kind == ModelKind.Enum ? Enum(model, @namespace) : Object(model, @namespace));

	static string Object(ModelType model, string @namespace)
	{
		var writer = new CodeWriter(@namespace);
		writer.Line();
		writer.Summary(model.Summary ?? $"The {model.Name} body.");
		writer.Open($"public partial class {model.Name}");

		var taken = new HashSet<string>(Inherited, StringComparer.Ordinal) { model.Name, "AdditionalData" };

		foreach (var property in model.Properties)
		{
			var member = Words.Unique(Words.Identifier(Words.Pascal(property.JsonName)), taken);

			writer.Summary(Describe(property.Description ?? $"The {property.JsonName} field.", property.Required));
			writer.Line($"[JsonPropertyName({CodeWriter.Literal(property.JsonName)})]");
			writer.Line($"public {TypeNames.Nullable(property.Type)} {member} {{ get; set; }}");
			writer.Line();
		}

		writer.Summary("Fields the description does not declare.");
		writer.Line("[JsonExtensionData]");
		writer.Line("public Dictionary<string, JsonElement>? AdditionalData { get; set; }");
		writer.Close();

		return writer.ToString();
	}

	static string Enum(ModelType model, string @namespace)
	{
		var writer = new CodeWriter(@namespace);
		writer.Line();
		writer.Summary(model.Summary ?? $"The values of {model.Name}; values not listed here are kept as they arrive.");
		writer.Line($"[JsonConverter(typeof(ExtensibleEnumConverter<{model.Name}>))]");
		writer.Open($"public readonly record struct {model.Name}(string Value) : IExtensibleEnum<{model.Name}>");

		var taken = new HashSet<string>(Inherited, StringComparer.Ordinal) { model.Name, "Value", "Create", "Deconstruct", "PrintMembers" };

		foreach (var value in model.EnumValues)
		{
			var member = Words.Unique(Words.Identifier(Words.Pascal(value)), taken);

			writer.Summary($"The value \"{value}\".");
			writer.Line($"public static {model.Name} {member} {{ get; }} = new({CodeWriter.Literal(value)});");
			writer.Line();
		}

		writer.Summary("Wraps any value, known or not.");
		writer.Line($"public static {model.Name} Create(string value) => new(value);");
		writer.Line();
		writer.Line("/// <inheritdoc/>");
		writer.Line("public override string ToString() => Value;");
		writer.Close();

		return writer.ToString();
	}

	static string Describe(string description, bool required)
		=> required ? description.TrimEnd().TrimEnd('.') + ". Required." : description;
}
