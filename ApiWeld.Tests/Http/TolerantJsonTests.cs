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
		[JsonPropertyName("day")] public DateOnly? Day { get; set; }
		[JsonPropertyName("moment")] public DateTimeOffset? Moment { get; set; }
		[JsonPropertyName("clock")] public TimeOnly? Clock { get; set; }
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

	[Theory]
	[InlineData("2026-10-27")]
	[InlineData("2026-10-27T00:00:00Z")]
	[InlineData("2026-10-27T23:30:00-05:00")]
	[InlineData("2026-10-27 08:15")]
	[InlineData("20261027")]
	[InlineData("20261027T101500Z")]
	[InlineData("2026/10/27")]
	[InlineData("10/27/2026")]
	[InlineData("10/27/2026 08:15:00")]
	public void Reads_a_date_or_the_date_part_of_a_date_time_into_a_date(string text)
	{
		Assert.Equal(new DateOnly(2026, 10, 27), Read($$"""{ "day": "{{text}}" }""").Day);
	}

	[Theory]
	[InlineData("2026-10-27T10:00:00Z", "2026-10-27T10:00:00.0000000+00:00")]
	[InlineData("2026-10-27T10:00:00.5+05:00", "2026-10-27T10:00:00.5000000+05:00")]
	[InlineData("2026-10-27T10:00:00,5+05:00", "2026-10-27T10:00:00.5000000+05:00")]
	[InlineData("2026-10-27T10:00:00.123456789Z", "2026-10-27T10:00:00.1234567+00:00")]
	[InlineData("2026-10-27t10:00:00z", "2026-10-27T10:00:00.0000000+00:00")]
	[InlineData("2026-10-27T10:00:00+0530", "2026-10-27T10:00:00.0000000+05:30")]
	[InlineData("2026-10-27T10:00:00-05", "2026-10-27T10:00:00.0000000-05:00")]
	[InlineData("2026-10-27T10:00:00", "2026-10-27T10:00:00.0000000+00:00")]
	[InlineData("2026-10-27T10:00", "2026-10-27T10:00:00.0000000+00:00")]
	[InlineData("2026-10-27", "2026-10-27T00:00:00.0000000+00:00")]
	[InlineData("20261027T100000Z", "2026-10-27T10:00:00.0000000+00:00")]
	[InlineData("20261027T1000-0500", "2026-10-27T10:00:00.0000000-05:00")]
	[InlineData("20261027", "2026-10-27T00:00:00.0000000+00:00")]
	[InlineData("2026/10/27 10:00:00", "2026-10-27T10:00:00.0000000+00:00")]
	[InlineData("10/27/2026", "2026-10-27T00:00:00.0000000+00:00")]
	[InlineData("10/27/2026 9:05", "2026-10-27T09:05:00.0000000+00:00")]
	[InlineData("1/2/2026 10:00:00-05:00", "2026-01-02T10:00:00.0000000-05:00")]
	public void Reads_iso_and_slash_forms_into_a_date_time_treating_a_missing_offset_as_utc(string text, string expected)
	{
		Assert.Equal(expected, Read($$"""{ "moment": "{{text}}" }""").Moment!.Value.ToString("O"));
	}

	[Theory]
	[InlineData("10:30", 10, 30, 0, 0)]
	[InlineData("10:30:15", 10, 30, 15, 0)]
	[InlineData("10:30:15.250", 10, 30, 15, 250)]
	[InlineData("10:30:15,250", 10, 30, 15, 250)]
	[InlineData("103015", 10, 30, 15, 0)]
	[InlineData("10:30:15Z", 10, 30, 15, 0)]
	[InlineData("10:30:15-05:00", 10, 30, 15, 0)]
	[InlineData("9:05", 9, 5, 0, 0)]
	public void Reads_hours_and_minutes_with_optional_seconds_into_a_time_keeping_the_clock_as_written(string text, int hour, int minute, int second, int millisecond)
	{
		Assert.Equal(new TimeOnly(hour, minute, second, millisecond), Read($$"""{ "clock": "{{text}}" }""").Clock);
	}

	[Fact]
	public void Turns_an_unparseable_date_or_time_into_null()
	{
		var sample = Read("""{ "day": "2026-02-30", "moment": "27/10/2026", "clock": "25:00", "text": "kept" }""");

		Assert.Null(sample.Day);
		Assert.Null(sample.Moment);
		Assert.Null(sample.Clock);
		Assert.Equal("kept", sample.Text);
	}

	[Theory]
	[InlineData("""{ "day": "" }""")]
	[InlineData("""{ "day": "2026-10-27Tjunk" }""")]
	[InlineData("""{ "day": "2026-1-2" }""")]
	[InlineData("""{ "day": "2026/13/01" }""")]
	[InlineData("""{ "moment": "Oct 27, 2026" }""")]
	[InlineData("""{ "moment": "10/27/2026 10:00 AM" }""")]
	[InlineData("""{ "moment": "2026-10-27T24:00:00Z" }""")]
	[InlineData("""{ "moment": "2026-10-27T10:00:00+15:00" }""")]
	[InlineData("""{ "moment": 1761559200 }""")]
	[InlineData("""{ "clock": "10:00:60" }""")]
	[InlineData("""{ "clock": "10:00.5" }""")]
	public void Turns_a_malformed_date_or_time_into_null(string json)
	{
		var sample = Read(json);

		Assert.Null(sample.Day);
		Assert.Null(sample.Moment);
		Assert.Null(sample.Clock);
	}

	[Fact]
	public void Writes_dates_and_times_in_their_iso_forms()
	{
		var sample = new Sample
		{
			Day = new DateOnly(2026, 10, 27),
			Moment = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.FromHours(-5)),
			Clock = new TimeOnly(10, 30, 15)
		};

		Assert.Equal("""{"day":"2026-10-27","moment":"2026-10-27T10:00:00-05:00","clock":"10:30:15"}""",
			JsonSerializer.Serialize(sample, ApiJson.Options));
	}

	sealed class Holder
	{
		[JsonPropertyName("items")] public List<Sample>? Items { get; set; }
		[JsonPropertyName("tags")] public List<string>? Tags { get; set; }
	}

	static Holder ReadHolder(string json) => JsonSerializer.Deserialize<Holder>(json, ApiJson.Options)!;

	[Fact]
	public void Reads_a_lone_object_where_a_list_is_declared_as_a_one_item_list()
	{
		Assert.Equal("only", Assert.Single(ReadHolder("""{ "items": { "text": "only" } }""").Items!).Text);
	}

	[Fact]
	public void Reads_a_top_level_lone_object_as_a_one_item_list()
	{
		var list = JsonSerializer.Deserialize<List<Sample>>("""{ "text": "only" }""", ApiJson.Options)!;

		Assert.Equal("only", Assert.Single(list).Text);
	}

	[Fact]
	public void Still_reads_arrays_with_tolerant_items_and_nulls()
	{
		var holder = ReadHolder("""{ "items": [ { "text": "a" }, { "count": "2" } ], "tags": [ "x", 3 ] }""");

		Assert.Equal(new[] { "a", null }, holder.Items!.Select(item => item.Text));
		Assert.Equal(2, holder.Items![1].Count);
		Assert.Equal(new[] { "x", "3" }, holder.Tags!);
		Assert.Null(ReadHolder("""{ "items": null }""").Items);
	}

	[Fact]
	public void Still_refuses_a_scalar_where_a_list_is_declared()
	{
		Assert.Throws<JsonException>(() => ReadHolder("""{ "tags": "x" }"""));
	}

	[Fact]
	public void Writes_a_list_as_an_array()
	{
		var json = JsonSerializer.Serialize(new Holder { Items = [new Sample { Text = "a" }] }, ApiJson.Options);

		Assert.Equal("""{"items":[{"text":"a"}]}""", json);
	}
}

[JsonConverter(typeof(ExtensibleEnumConverter<Color>))]
readonly record struct Color(string Value) : IExtensibleEnum<Color>
{
	public static Color Red { get; } = new("red");

	public static Color Create(string value) => new(value);

	public override string ToString() => Value;
}
