using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiWeld.Http;

/// <summary>A string enum that keeps values it does not know instead of failing on them.</summary>
public interface IExtensibleEnum<TSelf> where TSelf : struct, IExtensibleEnum<TSelf>
{
	/// <summary>The value as sent on the wire.</summary>
	string Value { get; }

	/// <summary>Wraps any value, known or not.</summary>
	static abstract TSelf Create(string value);
}

/// <summary>Reads and writes an <see cref="IExtensibleEnum{TSelf}"/> as its string value.</summary>
public sealed class ExtensibleEnumConverter<T> : JsonConverter<T> where T : struct, IExtensibleEnum<T>
{
	/// <inheritdoc/>
	public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> T.Create(TolerantStringConverter.ReadText(ref reader) ?? "");

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		=> writer.WriteStringValue(value.Value);
}
