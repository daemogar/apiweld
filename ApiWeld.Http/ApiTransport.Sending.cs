using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace ApiWeld.Http;

/// <summary>A response body and, for paged operations, its total-count header.</summary>
internal sealed record ApiExchange(string Body, int? Total);

public sealed partial class ApiTransport
{
	/// <summary>Sends a request and reads its body as <typeparamref name="T"/>; an empty body is default.</summary>
	public async Task<T?> SendAsync<T>(ApiOperation operation, HttpRequestMessage request, CancellationToken cancellationToken = default)
	{
		var exchange = await ExchangeAsync(operation, request, cancellationToken).ConfigureAwait(false);

		return string.IsNullOrWhiteSpace(exchange.Body) ? default : JsonSerializer.Deserialize<T>(exchange.Body, Json);
	}

	/// <summary>Sends a request whose response has no body worth reading.</summary>
	public async Task SendAsync(ApiOperation operation, HttpRequestMessage request, CancellationToken cancellationToken = default)
		=> await ExchangeAsync(operation, request, cancellationToken).ConfigureAwait(false);

	internal async Task<ApiExchange> ExchangeAsync(ApiOperation operation, HttpRequestMessage request, CancellationToken cancellationToken)
	{
		using (request)
		using (var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false))
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
				throw Fail(operation, response, body);

			CheckVersion(operation, response);

			return new(body, operation.Paging is { } paging ? TotalCount(response, paging.TotalHeader) : null);
		}
	}

	ApiResponseException Fail(ApiOperation operation, HttpResponseMessage response, string body)
	{
		var context = new ApiErrorContext(response.StatusCode, body, operation.Method, operation.Template, operation.Version);

		return operation.Errors.TryGetValue((int)response.StatusCode, out var factory)
			? factory(context, Json)
			: ApiResponseException.Create(context, Json);
	}

	void CheckVersion(ApiOperation operation, HttpResponseMessage response)
	{
		if (operation.Version is null)
			return;

		var received = MediaTypeVersion.Read(response.Content.Headers.ContentType?.ToString());

		if (received is null || received == operation.Version)
			return;

		if (ClientOptions.VersionMismatch == VersionMismatchBehavior.Throw)
			throw new MediaTypeMismatchException(operation.Method, operation.Template, operation.Version, received);

		Logger.LogWarning("{Method} {Template} asked for version {Requested} but the response is version {Received}.",
			operation.Method, operation.Template, operation.Version, received);
	}

	static int? TotalCount(HttpResponseMessage response, string header)
	{
		var values = response.Headers.TryGetValues(header, out var found) ? found
			: response.Content.Headers.TryGetValues(header, out found) ? found
			: null;

		return int.TryParse(values?.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ? total : null;
	}
}
