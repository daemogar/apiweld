using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Tests;

public class IsAbsentTests
{
	static JsonNode Parse(string json) => JsonNode.Parse(json)!;

	[Fact]
	public void An_object_permitting_no_properties_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("""{ "maxProperties": 0 }""")));

	[Fact]
	public void An_object_declaring_no_properties_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("""{ "type": "object" }""")));

	[Fact]
	public void A_zero_length_string_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("""{ "type": "string", "maxLength": 0 }""")));

	[Fact]
	public void A_bare_nullable_string_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("""{ "type": "string", "nullable": true }""")));

	[Fact]
	public void A_nullable_string_carrying_an_enum_is_not_absent()
		=> Assert.False(UnionCollapse.IsAbsent(
			Parse("""{ "type": "string", "nullable": true, "enum": ["a"] }""")));

	[Fact]
	public void A_nullable_string_carrying_a_pattern_is_not_absent()
		=> Assert.False(UnionCollapse.IsAbsent(
			Parse("""{ "type": "string", "nullable": true, "pattern": "^a$" }""")));

	[Fact]
	public void An_object_with_properties_is_not_absent()
		=> Assert.False(UnionCollapse.IsAbsent(
			Parse("""{ "type": "object", "properties": { "id": { "type": "string" } } }""")));

	[Fact]
	public void An_ordinary_string_is_not_absent()
		=> Assert.False(UnionCollapse.IsAbsent(Parse("""{ "type": "string" }""")));

	[Fact]
	public void A_node_that_is_not_an_object_is_not_absent()
		=> Assert.False(UnionCollapse.IsAbsent(Parse("true")));

	[Fact]
	public void A_schema_that_accepts_nothing_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("false")));

	[Fact]
	public void A_null_type_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("""{ "type": "null" }""")));

	[Fact]
	public void A_bare_string_or_null_type_array_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("""{ "type": ["string", "null"] }""")));

	[Fact]
	public void A_string_or_null_type_array_carrying_an_enum_is_not_absent()
		=> Assert.False(UnionCollapse.IsAbsent(Parse("""{ "type": ["string", "null"], "enum": ["a"] }""")));

	[Fact]
	public void An_object_or_null_type_array_declaring_no_properties_is_absent()
		=> Assert.True(UnionCollapse.IsAbsent(Parse("""{ "type": ["object", "null"] }""")));

	[Fact]
	public void A_type_array_of_two_real_types_is_not_absent()
		=> Assert.False(UnionCollapse.IsAbsent(Parse("""{ "type": ["string", "integer"] }""")));
}
