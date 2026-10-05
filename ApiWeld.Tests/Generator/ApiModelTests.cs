using ApiWeld.Generator;
using ApiWeld.Generator.Model;

namespace ApiWeld.Tests.Generator;

public class ApiModelTests
{
	static readonly Manifest Plain = Descriptions.Manifest("/api", "/query");

	const string V1 = "application/vnd.example.v1+json";

	const string Paging = """
		[ { "name": "offset", "in": "query", "schema": { "type": "integer" } },
		  { "name": "limit", "in": "query", "schema": { "type": "integer" } },
		  { "name": "criteria", "in": "query", "schema": { "type": "string" } } ]
		""";

	static OperationModel Only(ApiModel model, string member, string method, params string[] keys)
		=> Descriptions.Operation(Descriptions.Node(model, keys), member, method);

	[Fact]
	public void Merges_paths_from_several_descriptions_into_one_tree()
	{
		var (model, _) = Descriptions.Build(Plain,
			("widgets.json", Descriptions.Document($$"""
				{ "/api/widgets": { "get": {{Descriptions.Returns(V1, Descriptions.Things)}} },
				  "/api/widgets/{id}": { "get": {{Descriptions.Returns(V1, Descriptions.Thing)}} } }
				""")),
			("parts.json", Descriptions.Document($$"""{ "/api/widgets/{widgetId}/parts": { "get": {{Descriptions.Returns(V1, Descriptions.Things)}} } }""")));

		var widgets = Descriptions.Node(model, "widgets");

		Assert.Equal(new[] { "{}" }, widgets.Children.Keys);
		Assert.Equal(new[] { "parts" }, widgets.Children["{}"].Children.Keys);
		Assert.Equal("api/widgets/{widgetId}/parts", Only(model, "V1", "Get", "widgets", "{}", "parts").Template);
	}

	[Fact]
	public void Types_an_indexer_from_a_uuid_parameter()
	{
		const string uuid = """[ { "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ]""";

		var (model, _) = Descriptions.Build(Plain, ("widgets.json", Descriptions.Document($$"""{ "/api/widgets/{id}": { "get": {{Descriptions.Returns(V1, Descriptions.Thing, uuid)}} } }""")));

		Assert.Equal("Guid", Descriptions.Node(model, "widgets", "{}").ParameterType);
	}

	[Fact]
	public void Merges_parameter_segments_by_position_and_falls_back_to_string_when_their_types_differ()
	{
		const string uuid = """[ { "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ]""";
		const string text = """[ { "name": "widgetId", "in": "path", "required": true, "schema": { "type": "string" } } ]""";

		var (model, diagnostics) = Descriptions.Build(Plain,
			("a.json", Descriptions.Document($$"""{ "/api/widgets/{id}": { "get": {{Descriptions.Returns(V1, Descriptions.Thing, uuid)}} } }""")),
			("b.json", Descriptions.Document($$"""{ "/api/widgets/{widgetId}/parts": { "get": {{Descriptions.Returns(V1, Descriptions.Things, text)}} } }""")));

		var item = Descriptions.Node(model, "widgets", "{}");

		Assert.Equal("id", item.ParameterName);
		Assert.Equal("string", item.ParameterType);
		Assert.Contains(diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("{widgetId}"));
		Assert.DoesNotContain(diagnostics, d => d.Severity == Severity.Error);
		Assert.Equal(new[] { "{}" }, Descriptions.Node(model, "widgets").Children.Keys);
	}

	[Fact]
	public void Gives_a_digit_leading_segment_a_valid_class_base()
	{
		var (model, _) = Descriptions.Build(Plain, ("models.json", Descriptions.Document(
			$$"""{ "/api/3d-models": { "get": {{Descriptions.Returns(V1, Descriptions.Things)}} } }""")));

		Assert.Equal("_3DModels", Descriptions.Node(model, "3d-models").ClassBase);
	}

	[Theory]
	[InlineData("2XX")]
	[InlineData("2xx")]
	public void Reads_a_success_declared_as_a_range(string status)
	{
		var get = $$"""{ "responses": { "{{status}}": { "content": { "{{V1}}": { "schema": {{Descriptions.Thing}} } } } } }""";

		var (model, diagnostics) = Descriptions.Build(Plain, ("widgets.json", Descriptions.Document($$"""{ "/api/widgets": { "get": {{get}} } }""")));

		Assert.IsType<ModelRef>(Only(model, "V1", "Get", "widgets").Response);
		Assert.Empty(diagnostics);
	}

	[Fact]
	public void Prefers_a_numbered_success_over_a_range()
	{
		var get = $$"""
			{ "responses": {
				"200": { "content": { "{{V1}}": { "schema": {{Descriptions.Thing}} } } },
				"2XX": { "content": { "{{V1}}": { "schema": {{Descriptions.Things}} } } } } }
			""";

		var (model, diagnostics) = Descriptions.Build(Plain, ("widgets.json", Descriptions.Document($$"""{ "/api/widgets": { "get": {{get}} } }""")));

		Assert.IsType<ModelRef>(Only(model, "V1", "Get", "widgets").Response);
		Assert.Empty(diagnostics);
	}

	[Fact]
	public void Warns_when_the_only_success_is_default()
	{
		var get = $$"""{ "responses": { "default": { "content": { "{{V1}}": { "schema": {{Descriptions.Thing}} } } } } }""";

		var (model, diagnostics) = Descriptions.Build(Plain, ("widgets.json", Descriptions.Document($$"""{ "/api/widgets": { "get": {{get}} } }""")));

		Assert.Null(Only(model, "V0", "Get", "widgets").Response);
		Assert.Contains(diagnostics, d => d.Severity == Severity.Warning
			&& d.Message == "widgets.json: GET /api/widgets declares its success response only as \"default\"; it is generated without a response body.");
	}

	[Theory]
	[InlineData("/api/widgets?kind=a")]
	[InlineData("/api/widgets#top")]
	public void Refuses_a_path_carrying_a_query_or_fragment(string path)
	{
		var (_, diagnostics) = Descriptions.Build(Plain, ("widgets.json", Descriptions.Document(
			$$"""{ "{{path}}": { "get": {{Descriptions.Returns(V1, Descriptions.Thing)}} } }""")));

		Assert.Contains(diagnostics, d => d.Severity == Severity.Error
			&& d.Message == $"widgets.json: GET {path} carries a query or fragment; that is not supported.");
	}

	[Fact]
	public void Warns_about_each_operation_under_an_unsupported_verb()
	{
		const string bare = """{ "responses": { "200": { "description": "Fine." } } }""";

		var (model, diagnostics) = Descriptions.Build(Plain, ("things.json", Descriptions.Document($$"""
			{ "/api/things": { "get": {{Descriptions.Returns(V1, Descriptions.Things)}}, "head": {{bare}}, "options": {{bare}}, "trace": {{bare}} } }
			""")));

		foreach (var verb in new[] { "HEAD", "OPTIONS", "TRACE" })
			Assert.Single(diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("things.json") && d.Message.Contains($"{verb} /api/things"));

		Assert.Equal(new[] { "Get" }, Descriptions.Node(model, "things").Versions["V1"].Select(o => o.Method));
	}

	[Fact]
	public void Strips_base_paths_and_merges_what_collides()
	{
		var (model, diagnostics) = Descriptions.Build(Plain, ("lookups.json", Descriptions.Document($$"""
			{ "/api/lookups": { "get": {{Descriptions.Returns(V1, Descriptions.Things)}} },
			  "/query/lookups": { "post": {{Descriptions.Returns(V1, Descriptions.Things)}} } }
			""")));

		var lookups = Descriptions.Node(model, "lookups");

		Assert.Equal(new[] { "Get", "Post" }, lookups.Versions["V1"].Select(o => o.Method).Order());
		Assert.Equal("query/lookups", Descriptions.Operation(lookups, "V1", "Post").Template);
		Assert.DoesNotContain(diagnostics, d => d.Severity == Severity.Error);
	}

	[Fact]
	public void Refuses_the_same_verb_at_the_same_version_on_one_path()
	{
		var get = Descriptions.Returns(V1, Descriptions.Things);

		var (_, diagnostics) = Descriptions.Build(Plain,
			("a.json", Descriptions.Document($$"""{ "/api/lookups": { "get": {{get}} } }""")),
			("b.json", Descriptions.Document($$"""{ "/query/lookups": { "get": {{get}} } }""")));

		var error = Assert.Single(diagnostics, d => d.Severity == Severity.Error);
		Assert.Contains("a.json", error.Message);
		Assert.Contains("b.json", error.Message);
	}

	[Theory]
	[InlineData("application/vnd.example.v15.0.0+json", "V15")]
	[InlineData("application/vnd.example.v12.6.0+json", "V12_6")]
	[InlineData("application/json", "V0")]
	public void Names_the_version_member_from_the_success_media_type(string mediaType, string member)
	{
		var (model, _) = Descriptions.Build(Plain, ("things.json", Descriptions.Document(
			$$"""{ "/api/things": { "get": {{Descriptions.Returns(mediaType, Descriptions.Things)}} } }""", version: "9.9.9")));

		Assert.Equal(new[] { member }, Descriptions.Node(model, "things").Versions.Keys);
	}

	[Fact]
	public void Warns_once_when_info_version_disagrees_with_the_media_types()
	{
		const string media = "application/vnd.example.v15.2.0+json";

		var (_, diagnostics) = Descriptions.Build(Plain, ("things.json", Descriptions.Document($$"""
			{ "/api/things": { "get": {{Descriptions.Returns(media, Descriptions.Things)}} },
			  "/api/things/{id}": { "get": {{Descriptions.Returns(media, Descriptions.Thing)}} } }
			""", version: "15.3.0")));

		Assert.Single(diagnostics, d => d.Message.Contains("info.version says 15.3"));
	}

	[Fact]
	public void Gives_a_bodyless_operation_the_version_its_description_uses()
	{
		var (model, _) = Descriptions.Build(Plain, ("widgets.json", Descriptions.Document($$"""
			{ "/api/widgets/{id}": {
				"get": {{Descriptions.Returns("application/vnd.example.v2+json", Descriptions.Thing)}},
				"delete": { "responses": { "204": { "description": "Gone." } } } } }
			""")));

		var delete = Only(model, "V2", "Delete", "widgets", "{}");

		Assert.Equal("2", delete.Version);
		Assert.Equal("application/vnd.example.v2+json", delete.Accept);
		Assert.Null(delete.Response);
	}

	[Fact]
	public void Keeps_request_and_error_media_types_on_the_operation()
	{
		const string put = """
			{ "requestBody": { "content": { "application/vnd.example.v11+json": { "schema": { "type": "object", "properties": { "name": { "type": "string" } } } } } },
			  "responses": {
				"200": { "content": { "application/vnd.example.v11.1.0+json": { "schema": { "type": "object", "properties": { "name": { "type": "string" } } } } } },
				"400": { "content": { "application/vnd.example.errors.v2+json": { "schema": { "type": "object", "properties": { "code": { "type": "string" } } } } } } } }
			""";

		var (model, _) = Descriptions.Build(Plain, ("addresses.json", Descriptions.Document($$"""{ "/api/addresses/{id}": { "put": {{put}} } }""")));

		var node = Descriptions.Node(model, "addresses", "{}");
		var operation = Descriptions.Operation(node, "V11_1", "Put");

		Assert.Equal(new[] { "V11_1" }, node.Versions.Keys);
		Assert.Equal("11.1", operation.Version);
		Assert.Equal("application/vnd.example.v11+json", operation.ContentType);
		Assert.Equal("application/vnd.example.v11.1.0+json, application/vnd.example.errors.v2+json", operation.Accept);
		Assert.IsType<ModelRef>(operation.Errors[400]);
	}

	[Fact]
	public void Prefers_the_versioned_media_type_over_plain_json()
	{
		const string get = """
			{ "responses": { "200": { "content": {
				"application/json": { "schema": { "type": "array", "items": { "type": "string" } } },
				"application/vnd.example.v7+json": { "schema": { "type": "array", "items": { "type": "string" } } } } } } }
			""";

		var (model, _) = Descriptions.Build(Plain, ("things.json", Descriptions.Document($$"""{ "/api/things": { "get": {{get}} } }""", version: "7")));

		var operation = Only(model, "V7", "Get", "things");

		Assert.Equal("application/vnd.example.v7+json", operation.MediaType);
		Assert.Equal("application/vnd.example.v7+json", operation.Accept);
	}

	[Fact]
	public void Treats_a_list_with_offset_and_limit_as_paged_and_leaves_offset_to_the_walk()
	{
		var (model, _) = Descriptions.Build(Plain, ("things.json", Descriptions.Document(
			$$"""{ "/api/things": { "get": {{Descriptions.Returns("application/json", Descriptions.Things, Paging)}} } }""")));

		var operation = Only(model, "V0", "Get", "things");

		Assert.True(operation.IsPaged);
		Assert.Equal(new[] { "limit", "criteria" }, operation.Parameters.Select(p => p.Name));
	}

	[Fact]
	public void Does_not_page_without_both_parameters_a_list_and_a_get()
	{
		const string limitOnly = """[ { "name": "limit", "in": "query", "schema": { "type": "integer" } } ]""";

		var (model, _) = Descriptions.Build(Plain, ("things.json", Descriptions.Document($$"""
			{ "/api/a": { "get": {{Descriptions.Returns("application/json", Descriptions.Things, limitOnly)}} },
			  "/api/b": { "get": {{Descriptions.Returns("application/json", Descriptions.Thing, Paging)}} },
			  "/api/c": { "post": {{Descriptions.Returns("application/json", Descriptions.Things, Paging)}} } }
			""")));

		Assert.False(Only(model, "V0", "Get", "a").IsPaged);
		Assert.False(Only(model, "V0", "Get", "b").IsPaged);
		Assert.False(Only(model, "V0", "Post", "c").IsPaged);
	}

	[Fact]
	public void Absorbs_accept_and_content_type_headers_and_keeps_other_headers()
	{
		const string headers = """
			[ { "name": "Accept", "in": "header", "schema": { "type": "string" } },
			  { "name": "content type", "in": "header", "schema": { "type": "string" } },
			  { "name": "X-Trace", "in": "header", "schema": { "type": "string" } } ]
			""";

		var (model, _) = Descriptions.Build(Plain, ("things.json", Descriptions.Document(
			$$"""{ "/api/things": { "get": {{Descriptions.Returns("application/json", Descriptions.Things, headers)}} } }""")));

		var parameter = Assert.Single(Only(model, "V0", "Get", "things").Parameters);
		Assert.Equal(("X-Trace", "header"), (parameter.Name, parameter.In));
	}

	[Fact]
	public void Refuses_an_operation_at_the_root_once_base_paths_are_removed()
	{
		var (_, diagnostics) = Descriptions.Build(Plain, ("root.json", Descriptions.Document(
			$$"""{ "/api": { "get": {{Descriptions.Returns("application/json", Descriptions.Things)}} } }""")));

		Assert.Contains(diagnostics, d => d.Severity == Severity.Error && d.Message.Contains("root of the API"));
	}

	[Theory]
	[InlineData("{id}.json")]
	[InlineData("{a}-{b}")]
	[InlineData("v{id}")]
	public void Refuses_a_path_segment_mixing_text_and_parameters(string segment)
	{
		var (model, diagnostics) = Descriptions.Build(Plain, ("things.json", Descriptions.Document(
			$$"""{ "/api/things/{{segment}}": { "get": {{Descriptions.Returns("application/json", Descriptions.Things)}} } }""")));

		var error = Assert.Single(diagnostics, d => d.Severity == Severity.Error);
		Assert.Contains("things.json", error.Message);
		Assert.Contains(segment, error.Message);
		Assert.Empty(model.Root.Children);
	}

	[Fact]
	public void Reports_a_description_that_cannot_be_read()
	{
		var (_, diagnostics) = Descriptions.Build(Plain, ("bad.json", "{ not json"));

		Assert.Contains(diagnostics, d => d.Severity == Severity.Error && d.Message.StartsWith("bad.json: could not normalize"));
	}
}
