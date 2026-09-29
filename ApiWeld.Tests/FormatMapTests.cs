using ApiWeld.Core;

namespace ApiWeld.Tests;

public class FormatMapTests
{
	[Fact]
	public void Substitutes_a_non_standard_identifier_format_for_its_standard_spelling()
	{
		var result = FormatMap.Apply("""{ "format": "guid" }""");

		Assert.Equal("""{ "format": "uuid" }""", result);
	}

	[Fact]
	public void Substitutes_a_non_standard_string_format_for_a_plain_string()
	{
		var result = FormatMap.Apply("""{ "format": "email" }""");

		Assert.Equal("""{ "format": "string" }""", result);
	}

	[Fact]
	public void Leaves_a_format_it_does_not_recognize_alone()
	{
		var result = FormatMap.Apply("""{ "format": "date-time" }""");

		Assert.Equal("""{ "format": "date-time" }""", result);
	}

	[Fact]
	public void Substitutes_every_occurrence_not_only_the_first()
	{
		var result = FormatMap.Apply("""[{ "format": "guid" }, { "format": "guid" }]""");

		Assert.Equal("""[{ "format": "uuid" }, { "format": "uuid" }]""", result);
	}

	// The substitution is literal text, matching one space after the colon. Pinned
	// rather than fixed: changing it is a behaviour change, and this test is what
	// would have to be revised deliberately to make it.
	[Fact]
	public void Does_not_match_when_the_spacing_differs()
	{
		var result = FormatMap.Apply("""{"format":"guid"}""");

		Assert.Equal("""{"format":"guid"}""", result);
	}
}
