using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiWeld.Http;

/// <summary>Reads a number or boolean where a string was declared, as its raw JSON text.</summary>
sealed class TolerantStringConverter : JsonConverter<string>
{
	public static string? ReadText(ref Utf8JsonReader reader) => reader.TokenType switch
	{
		JsonTokenType.String => reader.GetString(),
		JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
		JsonTokenType.True => "true",
		JsonTokenType.False => "false",
		JsonTokenType.Null => null,
		_ => throw new JsonException($"Cannot read a JSON {reader.TokenType} as a string.")
	};

	public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadText(ref reader);

	public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);

	// Dictionary keys (including extension data) pass through untouched.
	public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;

	public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName(value);
}

/// <summary>Reads a scalar from its declared JSON kind, or parses it from text; unparseable text is null.</summary>
abstract class TolerantScalarConverter<T> : JsonConverter<T> where T : struct
{
	protected abstract bool TryReadNative(ref Utf8JsonReader reader, out T value);

	protected abstract bool TryParse(string text, out T value);

	protected abstract void WriteNative(Utf8JsonWriter writer, T value);

	public T? ReadNullable(ref Utf8JsonReader reader)
	{
		if (reader.TokenType == JsonTokenType.String)
			return TryParse(reader.GetString()!, out var parsed) ? parsed : null;

		if (TryReadNative(ref reader, out var value))
			return value;

		// A number in a form the native read refuses, such as 3.0 for an integer, is read like text.
		if (reader.TokenType == JsonTokenType.Number)
			return TryParse(TolerantStringConverter.ReadText(ref reader)!, out var coerced) ? coerced : null;

		throw new JsonException($"Cannot read a JSON {reader.TokenType} as {typeof(T).Name}.");
	}

	public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadNullable(ref reader) ?? default;

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => WriteNative(writer, value);
}

/// <summary>The nullable form of a tolerant scalar, so unparseable text becomes null rather than zero.</summary>
sealed class TolerantNullableConverter<T>(TolerantScalarConverter<T> inner) : JsonConverter<T?> where T : struct
{
	public override bool HandleNull => true;

	public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> reader.TokenType == JsonTokenType.Null ? null : inner.ReadNullable(ref reader);

	public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
	{
		if (value is { } present)
			inner.Write(writer, present, options);
		else
			writer.WriteNullValue();
	}
}

sealed class TolerantInt32Converter : TolerantScalarConverter<int>
{
	protected override bool TryReadNative(ref Utf8JsonReader reader, out int value)
	{
		value = 0;

		return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out value);
	}

	protected override bool TryParse(string text, out int value)
	{
		if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			return true;

		var fits = WholeNumber.TryParse(text, out var number) && number >= int.MinValue && number <= int.MaxValue;
		value = fits ? (int)number : 0;

		return fits;
	}

	protected override void WriteNative(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);
}

sealed class TolerantInt64Converter : TolerantScalarConverter<long>
{
	protected override bool TryReadNative(ref Utf8JsonReader reader, out long value)
	{
		value = 0;

		return reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out value);
	}

	protected override bool TryParse(string text, out long value)
	{
		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			return true;

		var fits = WholeNumber.TryParse(text, out var number) && number >= long.MinValue && number <= long.MaxValue;
		value = fits ? (long)number : 0;

		return fits;
	}

	protected override void WriteNative(Utf8JsonWriter writer, long value) => writer.WriteNumberValue(value);
}

sealed class TolerantDecimalConverter : TolerantScalarConverter<decimal>
{
	protected override bool TryReadNative(ref Utf8JsonReader reader, out decimal value)
	{
		value = 0;

		return reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out value);
	}

	protected override bool TryParse(string text, out decimal value) => decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value);

	protected override void WriteNative(Utf8JsonWriter writer, decimal value) => writer.WriteNumberValue(value);
}

sealed class TolerantDoubleConverter : TolerantScalarConverter<double>
{
	protected override bool TryReadNative(ref Utf8JsonReader reader, out double value)
	{
		value = 0;

		return reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out value);
	}

	protected override bool TryParse(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

	protected override void WriteNative(Utf8JsonWriter writer, double value) => writer.WriteNumberValue(value);
}

sealed class TolerantBooleanConverter : TolerantScalarConverter<bool>
{
	protected override bool TryReadNative(ref Utf8JsonReader reader, out bool value)
	{
		value = reader.TokenType == JsonTokenType.True;

		return reader.TokenType is JsonTokenType.True or JsonTokenType.False;
	}

	protected override bool TryParse(string text, out bool value) => bool.TryParse(text, out value);

	protected override void WriteNative(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);
}

/// <summary>Parses number text whose value is whole, whatever its form: <c>3.0</c> and <c>1e2</c> as well as <c>3</c>.</summary>
static class WholeNumber
{
	public static bool TryParse(string text, out decimal value)
		=> decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value == decimal.Truncate(value);
}
