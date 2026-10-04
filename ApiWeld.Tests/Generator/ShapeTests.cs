using System.Text.Json.Nodes;

using ApiWeld.Generator;
using ApiWeld.Generator.Model;

namespace ApiWeld.Tests.Generator;

public class ShapeTests
{
	static readonly JsonObject Document = (JsonObject)JsonNode.Parse("""
		{ "components": { "schemas": {
			"errors": { "type": "object", "properties": { "code": { "type": "string" } } },
			"errors_1_0_0": { "type": "object", "properties": { "code": { "type": "string" }, "detail": { "type": "string" } } },
			"node": { "type": "object", "properties": { "name": { "type": "string" }, "child": { "$ref": "#/components/schemas/node" } } }
		} } }
		""")!;

	static NameContext Context(string root = "Widget", Direction direction = Direction.Response)
		=> new(root, "V2", "Get", [], direction == Direction.Request ? "Request" : "Response", direction, true, "widgets.json");

	static (ShapeBuilder Shapes, DiagnosticBag Diagnostics) Builder(Dictionary<string, string>? names = null)
	{
		var diagnostics = new DiagnosticBag();

		return (new ShapeBuilder(diagnostics, names ?? new Dictionary<string, string>()), diagnostics);
	}

	static TypeRef Build(ShapeBuilder shapes, string schema, NameContext? context = null)
		=> shapes.Build(JsonNode.Parse(schema), Document, context ?? Context());

	static ModelType Model(TypeRef type) => Assert.IsType<ModelRef>(type).Model;

	[Theory]
	[InlineData("""{ "type": "string" }""", "string")]
	[InlineData("""{ "type": "string", "format": "uuid" }""", "Guid")]
	[InlineData("""{ "type": "string", "format": "date-time" }""", "DateTimeOffset")]
	[InlineData("""{ "type": "string", "format": "date" }""", "DateOnly")]
	[InlineData("""{ "type": "integer" }""", "int")]
	[InlineData("""{ "type": "integer", "format": "int64" }""", "long")]
	[InlineData("""{ "type": "number" }""", "decimal")]
	[InlineData("""{ "type": "boolean" }""", "bool")]
	[InlineData("""{ }""", "JsonElement")]
	[InlineData("""{ "type": "object" }""", "JsonElement")]
	public void Maps_scalars(string schema, string expected)
	{
		Assert.Equal(expected, Assert.IsType<ScalarRef>(Build(Builder().Shapes, schema)).Name);
	}

	[Fact]
	public void Names_nested_objects_by_their_path_with_array_items_singular()
	{
		var root = Model(Build(Builder().Shapes, """
			{ "type": "object", "properties": { "addresses": { "type": "array", "items": {
				"type": "object", "properties": { "place": { "type": "object", "properties": { "country": { "type": "string" } } } } } } } }
			"""));
		var address = Model(Assert.IsType<ListRef>(root.Properties.Single().Type).Item);
		var place = Model(address.Properties.Single().Type);

		Assert.Equal("WidgetV2Response", root.Candidates.Single().Plain);
		Assert.Equal("WidgetV2AddressResponse", address.Candidates.Single().Plain);
		Assert.Equal("WidgetV2AddressPlaceResponse", place.Candidates.Single().Plain);
		Assert.Equal("WidgetV2GetAddressPlaceResponse", place.Candidates.Single().Qualified);
	}

	[Fact]
	public void Merges_identical_shapes_travelling_in_the_same_direction()
	{
		var shapes = Builder().Shapes;
		const string schema = """{ "type": "object", "properties": { "width": { "type": "number" } } }""";

		var widget = Model(Build(shapes, schema));
		var gadget = Model(Build(shapes, schema, Context("Gadget")));

		Assert.Same(widget, gadget);
		Assert.Equal(new[] { "WidgetV2Response", "GadgetV2Response" }, widget.Candidates.Select(c => c.Plain));
	}

	[Fact]
	public void Keeps_one_shape_used_in_both_directions_as_two_types()
	{
		var shapes = Builder().Shapes;
		const string schema = """{ "type": "object", "properties": { "width": { "type": "number" } } }""";

		Assert.NotSame(Model(Build(shapes, schema)), Model(Build(shapes, schema, Context(direction: Direction.Request))));
		Assert.Equal(2, shapes.Models.Count());
	}

	[Fact]
	public void Keeps_different_shapes_apart()
	{
		var shapes = Builder().Shapes;

		Build(shapes, """{ "type": "object", "properties": { "a": { "type": "string" } } }""");
		Build(shapes, """{ "type": "object", "properties": { "a": { "type": "string" }, "b": { "type": "string" } } }""");

		Assert.Equal(2, shapes.Models.Count());
	}

	[Fact]
	public void Keeps_shapes_apart_whose_names_or_values_mimic_the_key_syntax()
	{
		var shapes = Builder().Shapes;

		Build(shapes, """{ "type": "object", "properties": { "a": { "type": "string" }, "b": { "type": "string" } } }""");
		Build(shapes, """{ "type": "object", "properties": { "a:string,\"b": { "type": "string" } } }""");
		Build(shapes, """{ "type": "string", "enum": ["a", "b"] }""");
		Build(shapes, """{ "type": "string", "enum": ["a|b"] }""");
		Build(shapes, """{ "type": "string", "enum": ["a\",\"b"] }""");

		Assert.Equal(5, shapes.Models.Count());
	}

	[Fact]
	public void Merges_enums_listing_the_same_values_in_a_different_order()
	{
		var shapes = Builder().Shapes;

		var first = Model(Build(shapes, """{ "type": "string", "enum": ["active", "retired"] }"""));
		var second = Model(Build(shapes, """{ "type": "string", "enum": ["retired", "active"] }"""));

		Assert.Same(first, second);
		Assert.Equal(new[] { "active", "retired" }, first.EnumValues);
	}

	[Fact]
	public void Types_an_interior_self_reference_as_json_and_warns()
	{
		var document = (JsonObject)JsonNode.Parse("""
			{ "components": { "schemas": { "a": { "type": "object", "properties": {
				"self": { "type": "object", "properties": {
					"again": { "$ref": "#/components/schemas/a/properties/self" } } } } } } } }
			""")!;
		var (shapes, diagnostics) = Builder();

		var a = Model(shapes.Build(JsonNode.Parse("""{ "$ref": "#/components/schemas/a" }"""), document, Context()));
		var self = Model(a.Properties.Single().Type);

		Assert.Equal(ScalarRef.Json, self.Properties.Single().Type);
		Assert.Contains(diagnostics.Items, d => d.Message.Contains("refers to itself"));
	}

	[Fact]
	public void Builds_a_string_enum_as_an_enum_model()
	{
		var model = Model(Build(Builder().Shapes, """{ "type": "string", "enum": ["active", "retired", null] }"""));

		Assert.Equal(ModelKind.Enum, model.Kind);
		Assert.Equal(new[] { "active", "retired" }, model.EnumValues);
	}

	[Fact]
	public void Builds_maps_from_additional_properties()
	{
		var shapes = Builder().Shapes;

		Assert.Equal(ScalarRef.Int, Assert.IsType<MapRef>(Build(shapes, """{ "type": "object", "additionalProperties": { "type": "integer" } }""")).Value);
		Assert.Equal(ScalarRef.Json, Assert.IsType<MapRef>(Build(shapes, """{ "type": "object", "additionalProperties": true }""")).Value);
	}

	[Fact]
	public void Names_an_error_body_after_its_schema_and_error_version()
	{
		var context = new NameContext("WidgetError", "V1", "", [], "", Direction.Error, false, "widgets.json", ErrorRoot: true);

		var model = Model(Build(Builder().Shapes, """{ "$ref": "#/components/schemas/errors" }""", context));

		Assert.Equal("ErrorsV1", model.Candidates.Single().Plain);
	}

	[Fact]
	public void Rebases_a_schema_named_in_the_overrides()
	{
		var shapes = Builder(new() { ["errors_1_0_0"] = "Problem" }).Shapes;

		var model = Model(Build(shapes, """{ "$ref": "#/components/schemas/errors_1_0_0" }"""));

		Assert.Equal("ProblemV2Response", model.Candidates.Single().Plain);
	}

	[Fact]
	public void Types_a_recursive_reference_as_json_and_warns()
	{
		var (shapes, diagnostics) = Builder();

		var model = Model(Build(shapes, """{ "$ref": "#/components/schemas/node" }"""));

		Assert.Equal(ScalarRef.Json, model.Properties.Single(p => p.JsonName == "child").Type);
		Assert.Contains(diagnostics.Items, d => d.Message.Contains("node refers to itself"));
	}

	[Fact]
	public void Records_descriptions_and_required_properties()
	{
		var model = Model(Build(Builder().Shapes, """
			{ "type": "object", "description": "A widget.", "required": ["id"],
			  "properties": { "id": { "type": "string", "description": "Its key." }, "name": { "type": "string" } } }
			"""));

		Assert.Equal("A widget.", model.Summary);
		Assert.Equal("Its key.", model.Properties[0].Description);
		Assert.True(model.Properties[0].Required);
		Assert.False(model.Properties[1].Required);
	}
}
