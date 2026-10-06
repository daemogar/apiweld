using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Tests;

public class FormatMapTests
{
	static string Apply(string json) => FormatMap.Apply(JsonNode.Parse(json))!.ToJsonString();

	static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();

	[Fact]
	public void Substitutes_a_non_standard_identifier_format_for_its_standard_spelling()
		=> Assert.Equal(Compact("""{ "format": "uuid" }"""), Apply("""{ "format": "guid" }"""));

	[Fact]
	public void Substitutes_a_non_standard_string_format_for_a_plain_string()
		=> Assert.Equal(Compact("""{ "format": "string" }"""), Apply("""{ "format": "email" }"""));

	[Fact]
	public void Leaves_a_format_it_does_not_recognize_alone()
		=> Assert.Equal(Compact("""{ "format": "date-time" }"""), Apply("""{ "format": "date-time" }"""));

	[Fact]
	public void Substitutes_every_occurrence_not_only_the_first()
		=> Assert.Equal(Compact("""[{ "format": "uuid" }, { "items": { "format": "uuid" } }]"""),
			Apply("""[{ "format": "guid" }, { "items": { "format": "guid" } }]"""));

	[Fact]
	public void Matches_however_the_document_is_spaced()
		=> Assert.Equal(Compact("""{"format":"uuid"}"""), Apply("""{"format":"guid"}"""));

	[Theory]
	[InlineData("example")]
	[InlineData("examples")]
	[InlineData("default")]
	[InlineData("enum")]
	[InlineData("const")]
	[InlineData("x-sample")]
	public void Leaves_data_values_alone(string keyword)
	{
		var json = $$"""{ "{{keyword}}": { "format": "guid" } }""";

		Assert.Equal(Compact(json), Apply(json));
	}

	[Fact]
	public void Leaves_a_description_that_mentions_a_format_alone()
	{
		const string json = """{ "description": "\"format\": \"guid\"" }""";

		Assert.Equal(Compact(json), Apply(json));
	}

	[Fact]
	public void Leaves_a_property_named_format_alone()
	{
		const string json = """{ "properties": { "format": { "type": "string", "enum": ["guid"] } } }""";

		Assert.Equal(Compact(json), Apply(json));
	}
}
