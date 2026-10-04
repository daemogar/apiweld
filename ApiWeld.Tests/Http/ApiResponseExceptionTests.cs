using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class ApiResponseExceptionTests
{
	sealed class Problem
	{
		[JsonPropertyName("code")] public string? Code { get; set; }
	}

	static ApiErrorContext Context(string body)
		=> new(HttpStatusCode.BadRequest, body, HttpMethod.Get, "api/widgets", "2");

	[Fact]
	public void Reads_a_declared_error_body_into_its_type()
	{
		var exception = ApiResponseException<Problem>.Create(Context("""{ "code": "E1" }"""), ApiJson.Options);

		var typed = Assert.IsType<ApiResponseException<Problem>>(exception);
		Assert.Equal("E1", typed.Error?.Code);
		Assert.Equal(HttpStatusCode.BadRequest, typed.StatusCode);
		Assert.Equal("2", typed.Version);
	}

	[Fact]
	public void Keeps_an_unreadable_error_body_raw_with_the_parse_failure_inside()
	{
		var exception = ApiResponseException<Problem>.Create(Context("<html>oops</html>"), ApiJson.Options);

		var typed = Assert.IsType<ApiResponseException<Problem>>(exception);
		Assert.Null(typed.Error);
		Assert.Equal("<html>oops</html>", typed.Body);
		Assert.IsAssignableFrom<JsonException>(typed.InnerException);
	}

	[Fact]
	public void Treats_an_empty_error_body_as_no_error()
	{
		var typed = Assert.IsType<ApiResponseException<Problem>>(ApiResponseException<Problem>.Create(Context(""), ApiJson.Options));

		Assert.Null(typed.Error);
		Assert.Null(typed.InnerException);
	}

	[Fact]
	public void Names_the_method_template_and_status_in_the_message()
	{
		var exception = ApiResponseException.Create(Context("x"), ApiJson.Options);

		Assert.Equal(typeof(ApiResponseException), exception.GetType());
		Assert.Equal("GET api/widgets returned 400 BadRequest.", exception.Message);
	}

	[Fact]
	public void Names_both_versions_in_a_mismatch()
	{
		var exception = new MediaTypeMismatchException(HttpMethod.Get, "api/widgets", "2", "3");

		Assert.Equal("GET api/widgets asked for version 2 but the response is version 3.", exception.Message);
		Assert.Equal("2", exception.Requested);
		Assert.Equal("3", exception.Received);
	}
}
