using System.Collections;
using System.Globalization;

namespace ApiWeld.Http;

/// <summary>The query-string and header values a query object contributes to a request.</summary>
public sealed class ApiRequestParameters
{
	readonly List<KeyValuePair<string, string>> query = [];
	readonly Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

	internal IReadOnlyList<KeyValuePair<string, string>> QueryValues => query;

	internal IReadOnlyDictionary<string, string> Headers => headers;

	/// <summary>Sets a query parameter, replacing any earlier value; a sequence repeats the name, except a byte array, which is one base64 value; null removes it.</summary>
	public void Query(string name, object? value)
	{
		query.RemoveAll(pair => pair.Key == name);

		if (value is null)
			return;

		if (value is IEnumerable sequence and not string and not byte[])
		{
			foreach (var item in sequence)
				if (item is not null)
					query.Add(new(name, Format(item)));

			return;
		}

		query.Add(new(name, Format(value)));
	}

	/// <summary>Sets a header; a sequence other than a byte array is joined with commas; null removes it.</summary>
	public void Header(string name, object? value)
	{
		if (value is null)
			headers.Remove(name);
		else if (value is IEnumerable sequence and not string and not byte[])
			headers[name] = string.Join(',', sequence.Cast<object?>().OfType<object>().Select(Format));
		else
			headers[name] = Format(value);
	}

	/// <summary>Formats a value for a URL or header with the invariant culture.</summary>
	public static string Format(object value) => value switch
	{
		string text => text,
		bool flag => flag ? "true" : "false",
		Guid id => id.ToString("D"),
		DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
		DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
		DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		byte[] bytes => Convert.ToBase64String(bytes),
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? ""
	};
}
