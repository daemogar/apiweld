using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiWeld.Http;

/// <summary>Builds, sends and reads every request a generated client makes.</summary>
public sealed partial class ApiTransport
{
	/// <summary>A transport over <paramref name="http"/>, whose base address the operation templates are relative to.</summary>
	public ApiTransport(HttpClient http, ApiClientOptions options, ILogger? logger = null)
	{
		Http = http;
		ClientOptions = options;
		Logger = logger ?? NullLogger.Instance;
	}

	HttpClient Http { get; }

	ApiClientOptions ClientOptions { get; }

	ILogger Logger { get; }

	/// <summary>The serializer options bodies are read and written with.</summary>
	public JsonSerializerOptions Json => ApiJson.Options;

	/// <summary>The request an operation would send, without sending it.</summary>
	public HttpRequestMessage CreateRequest(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query = null, object? body = null)
	{
		var parameters = new ApiRequestParameters();
		query?.Apply(parameters);

		return CreateRequest(operation, path, parameters, body);
	}

	internal HttpRequestMessage CreateRequest(ApiOperation operation, IReadOnlyList<string> path, ApiRequestParameters parameters, object? body)
	{
		var url = BuildUrl(operation.Template, path, parameters.QueryValues);
		var request = new HttpRequestMessage(operation.Method, new Uri(url, UriKind.Relative));

		try
		{
			if (operation.Accept is { } accept)
				AddHeader(request.Headers, "Accept", accept);

			foreach (var (name, value) in parameters.Headers)
				AddHeader(request.Headers, name, value);

			if (body is not null)
			{
				var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Json));
				request.Content = content;
				AddHeader(content.Headers, "Content-Type", operation.ContentType ?? "application/json");
			}

			return request;
		}
		catch
		{
			request.Dispose();
			throw;
		}
	}

	/// <summary>Adds a header, refusing one the request cannot carry rather than dropping it.</summary>
	static void AddHeader(HttpHeaders headers, string name, string value)
	{
		if (value.AsSpan().IndexOfAny('\r', '\n') >= 0 || !headers.TryAddWithoutValidation(name, value))
			throw new ArgumentException($"The header \"{name}\" cannot be sent on a request.");
	}

	[GeneratedRegex(@"\{[^}]+\}")]
	private static partial Regex Placeholder();

	static string BuildUrl(string template, IReadOnlyList<string> path, IReadOnlyList<KeyValuePair<string, string>> query)
	{
		if (template.IndexOfAny(['?', '#']) >= 0)
			throw new ArgumentException($"The template {template} carries a query or fragment; query values belong on the query object.");

		var index = 0;
		var url = Placeholder().Replace(template.TrimStart('/'), _ => index < path.Count
			? Segment(template, path[index++])
			: throw new ArgumentException($"The template {template} needs more than the {path.Count} path values given."));

		if (index != path.Count)
			throw new ArgumentException($"The template {template} takes {index} path values, not {path.Count}.");

		if (query.Count == 0)
			return url;

		return url + "?" + string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
	}

	/// <summary>One escaped path value; empty, <c>.</c> and <c>..</c> are refused because they would address a different resource.</summary>
	static string Segment(string template, string value) => value is "" or "." or ".."
		? throw new ArgumentException($"The path value \"{value}\" for template {template} would address a different resource.")
		: Uri.EscapeDataString(value);
}
