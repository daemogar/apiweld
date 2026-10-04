using ApiWeld.Generator;
using ApiWeld.Generator.Model;

namespace ApiWeld.Tests.Generator;

/// <summary>Builds small synthetic descriptions and runs them through the model builder.</summary>
static class Descriptions
{
	public const string Thing = """{ "type": "object", "properties": { "name": { "type": "string" } } }""";

	public const string Things = """{ "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } }""";

	/// <summary>A description whose <c>paths</c> and <c>components.schemas</c> are the given JSON objects.</summary>
	public static string Document(string paths, string schemas = "{}", string version = "1.0.0")
		=> $$"""{ "openapi": "3.0.1", "info": { "title": "t", "version": "{{version}}" }, "paths": {{paths}}, "components": { "schemas": {{schemas}} } }""";

	/// <summary>An operation whose 200 response is <paramref name="schema"/>; <paramref name="extra"/>, when given, must end with a comma.</summary>
	public static string Returns(string mediaType, string schema, string parameters = "[]", string extra = "")
		=> $$"""{ "parameters": {{parameters}}, {{extra}} "responses": { "200": { "content": { "{{mediaType}}": { "schema": {{schema}} } } } } }""";

	public static Manifest Manifest(params string[] basePaths)
		=> new() { Descriptions = ["*.json"], Namespace = "Example", Client = "ExampleClient", BasePaths = basePaths };

	public static (ApiModel Model, IReadOnlyList<Diagnostic> Diagnostics) Build(Manifest manifest, params (string File, string Json)[] documents)
	{
		var diagnostics = new DiagnosticBag();
		var model = ApiModelBuilder.Build(manifest, documents.Select(d => new DescriptionSource(d.File, d.Json)), diagnostics);

		return (model, diagnostics.Items);
	}

	public static PathNode Node(ApiModel model, params string[] keys) => keys.Aggregate(model.Root, (node, key) => node.Children[key]);

	public static OperationModel Operation(PathNode node, string member, string method) => node.Versions[member].Single(o => o.Method == method);
}
