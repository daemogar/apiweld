using ApiWeld.Generator;
using ApiWeld.Generator.Model;

namespace ApiWeld.Tests.Generator;

public class NamingTests
{
	static readonly Manifest Plain = Descriptions.Manifest("/api");

	static IReadOnlyList<string> Names(ApiModel model) => [.. model.Models.Select(m => m.Name).Order(StringComparer.Ordinal)];

	static string List(string item) => $$"""{ "type": "array", "items": {{item}} }""";

	[Fact]
	public void Names_types_from_the_file_version_path_and_direction()
	{
		const string person = """
			{ "type": "object", "properties": { "addresses": { "type": "array", "items": {
				"type": "object", "properties": { "place": { "type": "object", "properties": { "country": { "type": "string" } } } } } } } }
			""";
		const string media = "application/vnd.example.v12.6.0+json";
		var put = $$"""
			{ "requestBody": { "content": { "{{media}}": { "schema": { "type": "object", "properties": { "nickname": { "type": "string" } } } } } },
			  "responses": { "200": { "content": { "{{media}}": { "schema": {{person}} } } } } }
			""";

		var (model, diagnostics) = Descriptions.Resolve(Plain, ("persons.json", Descriptions.Document($$"""{ "/api/persons/{id}": { "put": {{put}} } }""", version: "12.6.0")));

		Assert.Empty(diagnostics);
		Assert.Equal(new[] { "PersonV12_6AddressPlaceResponse", "PersonV12_6AddressResponse", "PersonV12_6Request", "PersonV12_6Response" }, Names(model));
	}

	[Fact]
	public void Merges_identical_shapes_across_files_under_the_shortest_name()
	{
		const string place = """{ "type": "object", "properties": { "country": { "type": "string" } } }""";
		var address = $$"""{ "type": "object", "properties": { "place": {{place}} } }""";
		var person = $$"""{ "type": "object", "properties": { "addresses": {{List(address)}} } }""";

		var (model, _) = Descriptions.Resolve(Plain,
			("persons.json", Descriptions.Document($$"""{ "/api/persons": { "get": {{Descriptions.Returns("application/vnd.example.v12.6.0+json", List(person))}} } }""", version: "12.6.0")),
			("addresses.json", Descriptions.Document($$"""{ "/api/addresses": { "get": {{Descriptions.Returns("application/vnd.example.v11.1.0+json", List(address))}} } }""", version: "11.1.0")));

		Assert.Equal(new[] { "AddressV11_1PlaceResponse", "AddressV11_1Response", "PersonV12_6Response" }, Names(model));
	}

	[Fact]
	public void Breaks_a_length_tie_in_ordinal_order()
	{
		const string dimensions = """{ "type": "object", "properties": { "width": { "type": "number" } } }""";
		var widget = Descriptions.Returns("application/vnd.example.v2+json", $$"""{ "type": "object", "properties": { "name": { "type": "string" }, "dimensions": {{dimensions}} } }""");
		var gadget = Descriptions.Returns("application/json", $$"""{ "type": "object", "properties": { "code": { "type": "string" }, "dimensions": {{dimensions}} } }""");

		var (model, _) = Descriptions.Resolve(Plain,
			("widgets.json", Descriptions.Document($$"""{ "/api/widgets": { "get": {{widget}} } }""", version: "2")),
			("gadgets.json", Descriptions.Document($$"""{ "/api/gadgets": { "get": {{gadget}} } }""")));

		Assert.Contains("GadgetV0DimensionsResponse", Names(model));
		Assert.DoesNotContain("WidgetV2DimensionsResponse", Names(model));
	}

	[Fact]
	public void Gives_a_differing_non_get_shape_its_verb()
	{
		const string richer = """{ "type": "object", "properties": { "name": { "type": "string" }, "id": { "type": "string" } } }""";

		var (model, diagnostics) = Descriptions.Resolve(Plain, ("things.json", Descriptions.Document($$"""
			{ "/api/things": {
				"get": {{Descriptions.Returns("application/vnd.example.v1+json", Descriptions.Thing)}},
				"post": {{Descriptions.Returns("application/vnd.example.v1+json", richer)}} } }
			""", version: "1")));

		Assert.Equal(new[] { "ThingV1PostResponse", "ThingV1Response" }, Names(model));
		Assert.Contains(diagnostics, d => d.Severity == Severity.Warning && d.Message.StartsWith("ThingV1Response:"));
	}

	[Fact]
	public void Refuses_a_clash_the_verb_cannot_settle()
	{
		static string Shape(string field) => $$"""{ "type": "object", "properties": { "{{field}}": { "type": "string" } } }""";
		const string media = "application/vnd.example.v1+json";

		var (_, diagnostics) = Descriptions.Resolve(Plain, ("things.json", Descriptions.Document($$"""
			{ "/api/things": { "get": {{Descriptions.Returns(media, Shape("a"))}} },
			  "/api/things/{id}": { "get": {{Descriptions.Returns(media, Shape("b"))}} },
			  "/api/things/{id}/more": { "get": {{Descriptions.Returns(media, Shape("c"))}} } }
			""", version: "1")));

		Assert.Contains(diagnostics, d => d.Severity == Severity.Error && d.Message.StartsWith("ThingV1GetResponse:") && d.Message.Contains("\"names\""));
	}

	[Fact]
	public void Applies_names_overrides_for_file_stems_and_component_schemas()
	{
		var manifest = Plain with { Names = new Dictionary<string, string> { ["widget-parts"] = "Part", ["errors_1_0_0"] = "Problem" } };
		const string get = """
			{ "responses": {
				"200": { "content": { "application/vnd.example.v1+json": { "schema": { "type": "array", "items": { "type": "object", "properties": { "sku": { "type": "string" } } } } } } },
				"400": { "content": { "application/vnd.example.errors.v1+json": { "schema": { "$ref": "#/components/schemas/errors_1_0_0" } } } } } }
			""";
		const string schemas = """{ "errors_1_0_0": { "type": "object", "properties": { "code": { "type": "string" } } } }""";

		var (model, _) = Descriptions.Resolve(manifest, ("widget-parts.json", Descriptions.Document($$"""{ "/api/parts": { "get": {{get}} } }""", schemas, version: "1")));

		Assert.Equal(new[] { "PartV1Response", "ProblemV1" }, Names(model));
	}

	[Fact]
	public void Names_error_bodies_after_their_schema_and_error_version()
	{
		static string Failing(string errorMedia) => $$"""
			{ "responses": {
				"200": { "content": { "application/json": { "schema": { "type": "string" } } } },
				"400": { "content": { "{{errorMedia}}": { "schema": { "$ref": "#/components/schemas/errors" } } } } } }
			""";

		var (model, _) = Descriptions.Resolve(Plain,
			("a.json", Descriptions.Document($$"""{ "/api/a": { "get": {{Failing("application/vnd.example.errors.v2+json")}} } }""",
				"""{ "errors": { "type": "object", "properties": { "code": { "type": "string" } } } }""")),
			("b.json", Descriptions.Document($$"""{ "/api/b": { "get": {{Failing("application/json")}} } }""",
				"""{ "errors": { "type": "object", "properties": { "code": { "type": "string" }, "detail": { "type": "string" } } } }""")));

		Assert.Equal(new[] { "Errors", "ErrorsV2" }, Names(model));
	}

	[Fact]
	public void Refuses_a_type_named_like_the_client()
	{
		var (_, diagnostics) = Descriptions.Resolve(Plain with { Client = "ThingV1Response" }, ("things.json", Descriptions.Document(
			$$"""{ "/api/things": { "get": {{Descriptions.Returns("application/vnd.example.v1+json", Descriptions.Thing)}} } }""", version: "1")));

		Assert.Contains(diagnostics, d => d.Severity == Severity.Error && d.Message.StartsWith("ThingV1Response:"));
	}
}
