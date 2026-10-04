using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiWeld.Http;

/// <summary>The serializer options every generated client uses.</summary>
/// <remarks>See README.md, "Tolerant JSON".</remarks>
public static class ApiJson
{
	/// <summary>Tolerant scalars, nulls omitted when writing, case-sensitive property names.</summary>
	public static JsonSerializerOptions Options { get; } = Create();

	static JsonSerializerOptions Create()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			PropertyNameCaseInsensitive = false
		};

		options.Converters.Add(new TolerantStringConverter());
		Add(options, new TolerantInt32Converter());
		Add(options, new TolerantInt64Converter());
		Add(options, new TolerantDecimalConverter());
		Add(options, new TolerantDoubleConverter());
		Add(options, new TolerantBooleanConverter());
		options.MakeReadOnly(populateMissingResolver: true);

		return options;
	}

	static void Add<T>(JsonSerializerOptions options, TolerantScalarConverter<T> converter) where T : struct
	{
		options.Converters.Add(converter);
		options.Converters.Add(new TolerantNullableConverter<T>(converter));
	}
}
