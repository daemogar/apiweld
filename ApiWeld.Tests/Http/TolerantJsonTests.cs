using System.Text.Json;
using System.Text.Json.Serialization;

using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class TolerantJsonTests
{
	sealed class Sample
	{
		[JsonPropertyName("text")] public string? Text { get; set; }
		[JsonPropertyName("count")] public int? Count { get; set; }
		[JsonPropertyName("big")] public long? Big { get; set; }
		[JsonPropertyName("amount")] public decimal? Amount { get; set; }
		[JsonPropertyName("ratio")] public double? Ratio { get; set; }
		[JsonPropertyName("flag")] public bool? Flag { get; set; }
		[JsonPropertyName("color")] public Color? Color { get; set; }
		[JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
	}

	static Sample Read(string json) => JsonSerializer.Deserialize<Sample>(json, ApiJson.Options)!;

	[Theory]
	[InlineData("""{ "text": 2026 }""", "2026")]
	[InlineData("""{ "text": 1.50 }""", "1.50")]
	[InlineData("""{ "text": true }""", "true")]
	[InlineData("""{ "text": false }""", "false")]
	[InlineData("""{ "text": "plain" }""", "plain")]
	public void Reads_a_number_or_boolean_into_a_string_as_its_raw_text(string json, string expected)
	{
		Assert.Equal(expected, Read(json).Text);
	}

	[Fact]
	public void Parses_numeric_text_into_numbers_with_the_invariant_culture()
	{
		var sample = Read("""{ "count": "42", "big": "9000000000", "amount": "12.50", "ratio": "0.25" }""");

		Assert.Equal(42, sample.Count);
		Assert.Equal(9_000_000_000L, sample.Big);
		Assert.Equal(12.50m, sample.Amount);
		Assert.Equal(0.25, sample.Ratio);
	}

	[Theory]
	[InlineData("""{ "flag": "true" }""", true)]
	[InlineData("""{ "flag": "False" }""", false)]
	[InlineData("""{ "flag": false }""", false)]
	public void Parses_boolean_text(string json, bool expected)
	{
		Assert.Equal(expected, Read(json).Flag);
	}

	[Fact]
	public void Turns_unparseable_text_into_null()
	{
		var sample = Read("""{ "count": "abc", "amount": "", "flag": "maybe" }""");

		Assert.Null(sample.Count);
		Assert.Null(sample.Amount);
		Assert.Null(sample.Flag);
	}

	[Theory]
	[InlineData("""{ "count": 3.0, "big": 3.0 }""")]
	[InlineData("""{ "count": 3e0, "big": 3e0 }""")]
	[InlineData("""{ "count": "3.0", "big": "3.0" }""")]
	public void Reads_a_whole_number_in_any_form_into_an_integer(string json)
	{
		var sample = Read(json);

		Assert.Equal(3, sample.Count);
		Assert.Equal(3L, sample.Big);
	}

	[Theory]
	[InlineData("""{ "count": 3.00000000000000000000000000001 }""", null)]
	[InlineData("""{ "count": "3.00000000000000000000000000001" }""", null)]
	[InlineData("""{ "count": 1e-400 }""", null)]
	[InlineData("""{ "count": 300e-2 }""", 3)]
	public void Reads_a_number_as_whole_only_when_every_digit_after_the_point_is_zero(string json, int? expected)
	{
		Assert.Equal(expected, Read(json).Count);
	}

	[Fact]
	public void Turns_a_number_that_does_not_fit_into_null()
	{
		var sample = Read("""{ "count": 3.5, "big": 1e20, "flag": 1, "text": "kept" }""");

		Assert.Null(sample.Count);
		Assert.Null(sample.Big);
		Assert.Null(sample.Flag);
		Assert.Equal("kept", sample.Text);
	}

	[Fact]
	public void Still_reads_values_of_the_declared_kind()
	{
		var sample = Read("""{ "count": 3, "amount": 1.5, "flag": true, "text": null }""");

		Assert.Equal(3, sample.Count);
		Assert.Equal(1.5m, sample.Amount);
		Assert.True(sample.Flag);
		Assert.Null(sample.Text);
	}

	[Fact]
	public void Keeps_a_misnamed_field_visible_in_additional_data()
	{
		var sample = Read("""{ "Text": "wrong case" }""");

		Assert.Null(sample.Text);
		Assert.True(sample.AdditionalData!.ContainsKey("Text"));
	}

	[Fact]
	public void Omits_nulls_when_writing()
	{
		Assert.Equal("""{"count":1}""", JsonSerializer.Serialize(new Sample { Count = 1 }, ApiJson.Options));
	}

	[Fact]
	public void Reads_known_and_unknown_enum_values_and_writes_them_back()
	{
		Assert.Equal(Color.Red, Read("""{ "color": "red" }""").Color);

		var unknown = Read("""{ "color": "ultraviolet" }""").Color;

		Assert.Equal("ultraviolet", unknown?.Value);
		Assert.Equal("""{"color":"ultraviolet"}""", JsonSerializer.Serialize(new Sample { Color = unknown }, ApiJson.Options));
	}
}

[JsonConverter(typeof(ExtensibleEnumConverter<Color>))]
readonly record struct Color(string Value) : IExtensibleEnum<Color>
{
	public static Color Red { get; } = new("red");

	public static Color Create(string value) => new(value);

	public override string ToString() => Value;
}
