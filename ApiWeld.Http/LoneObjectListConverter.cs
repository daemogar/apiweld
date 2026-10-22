using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiWeld.Http;

/// <summary>Reads a lone JSON object where a list is declared as a one-item list.</summary>
/// <remarks>See README.md, "Tolerant JSON".</remarks>
sealed class LoneObjectListConverterFactory : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert)
		=> typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(List<>);

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
		=> (JsonConverter)Activator.CreateInstance(typeof(LoneObjectListConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

/// <summary>A <c>List&lt;T&gt;</c> that also accepts a lone object.</summary>
sealed class LoneObjectListConverter<T> : JsonConverter<List<T>>
{
	public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.StartObject)
			return [JsonSerializer.Deserialize<T>(ref reader, options)!];

		if (reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException($"Expected an array or an object for {typeToConvert}, found {reader.TokenType}.");

		var list = new List<T>();

		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
			list.Add(JsonSerializer.Deserialize<T>(ref reader, options)!);

		return list;
	}

	public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach (var item in value)
			JsonSerializer.Serialize(writer, item, options);

		writer.WriteEndArray();
	}
}
