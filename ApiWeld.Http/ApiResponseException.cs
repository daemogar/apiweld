using System.Net;
using System.Text.Json;

namespace ApiWeld.Http;

/// <summary>What the runtime knows about a failed response when it builds the exception.</summary>
public sealed record ApiErrorContext(HttpStatusCode StatusCode, string Body, HttpMethod Method, string Template, string? Version);

/// <summary>Builds the exception for one declared error status.</summary>
public delegate ApiResponseException ApiErrorFactory(ApiErrorContext context, JsonSerializerOptions json);

/// <summary>A response outside the 2xx range.</summary>
public class ApiResponseException(ApiErrorContext context, Exception? inner = null)
	: Exception($"{context.Method} {context.Template} returned {(int)context.StatusCode} {context.StatusCode}.", inner)
{
	/// <summary>The response status.</summary>
	public HttpStatusCode StatusCode => context.StatusCode;

	/// <summary>The response body as received.</summary>
	public string Body => context.Body;

	/// <summary>The request method.</summary>
	public HttpMethod Method => context.Method;

	/// <summary>The operation's URL template.</summary>
	public string Template => context.Template;

	/// <summary>The normalized version the request asked for, or null.</summary>
	public string? Version => context.Version;

	/// <summary>The factory for a status the description does not declare.</summary>
	public static ApiResponseException Create(ApiErrorContext context, JsonSerializerOptions json) => new(context);
}

/// <summary>A response with a declared error status, its body read into <typeparamref name="TError"/>.</summary>
public sealed class ApiResponseException<TError>(ApiErrorContext context, TError? error, Exception? inner = null)
	: ApiResponseException(context, inner) where TError : class
{
	/// <summary>The error body, or null when it was empty or unreadable.</summary>
	public TError? Error { get; } = error;

	/// <summary>The factory for a status whose body is declared as <typeparamref name="TError"/>.</summary>
	public static new ApiResponseException Create(ApiErrorContext context, JsonSerializerOptions json)
	{
		if (string.IsNullOrWhiteSpace(context.Body))
			return new ApiResponseException<TError>(context, null);

		try
		{
			return new ApiResponseException<TError>(context, JsonSerializer.Deserialize<TError>(context.Body, json));
		}
		catch (JsonException exception)
		{
			return new ApiResponseException<TError>(context, null, exception);
		}
	}
}
