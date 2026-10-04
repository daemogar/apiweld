using ApiWeld.Generator;
using ApiWeld.Generator.Emit;

namespace ApiWeld.Tests.Generator;

public class ModelEmitterTests
{
	const string Widget = """
		{ "type": "object", "description": "A widget & its <parts>.", "required": ["id"], "properties": {
			"id": { "type": "string", "format": "uuid", "description": "Its key." },
			"name": { "type": "string" },
			"Name": { "type": "string" },
			"2fa": { "type": "boolean" },
			"status": { "type": "string", "enum": ["active", "retired", "2nd-hand", ""] },
			"sizes": { "type": "array", "items": { "type": "integer", "format": "int64" } },
			"labels": { "type": "object", "additionalProperties": { "type": "string" } },
			"made": { "type": "string", "format": "date" },
			"extra": { }
		} }
		""";

	static IReadOnlyList<GeneratedFile> Emit()
	{
		var (model, diagnostics) = Descriptions.Resolve(Descriptions.Manifest("/api"), ("widgets.json", Descriptions.Document(
			$$"""{ "/api/widgets/{id}": { "get": {{Descriptions.Returns("application/vnd.example.v2+json", Widget)}} } }""", version: "2")));

		Assert.Empty(diagnostics);

		return [.. model.Models.OrderBy(m => m.Name, StringComparer.Ordinal).Select(m => ModelEmitter.Emit(m, "Example"))];
	}

	[Fact]
	public Task Writes_a_class_per_object_and_a_struct_per_enum()
		=> Verify(string.Join("\n", Emit().Select(file => $"==== {file.Path} ====\n{file.Content}"))).UseDirectory("Snapshots");

	[Fact]
	public void The_models_compile_without_errors_or_warnings()
	{
		var (_, diagnostics) = Compiler.Compile(Emit().Select(file => (file.Path, file.Content)));

		Assert.Empty(diagnostics);
	}
}
