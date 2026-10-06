using System.Globalization;

using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class RequestTests
{
	sealed class SampleQuery : IApiQuery
	{
		public string? Criteria { get; set; }
		public int? Limit { get; set; }
		public List<string>? Tags { get; set; }
		public string? Trace { get; set; }

		void IApiQuery.Apply(ApiRequestParameters parameters)
		{
			parameters.Query("criteria", Criteria);
			parameters.Query("limit", Limit);
			parameters.Query("tag", Tags);
			parameters.Header("X-Trace", Trace);
		}
	}

	sealed class HeaderQuery(string name, string value) : IApiQuery
	{
		public void Apply(ApiRequestParameters parameters) => parameters.Header(name, value);
	}

	sealed class BytesQuery : IApiQuery
	{
		public void Apply(ApiRequestParameters parameters) => parameters.Query("data", new byte[] { 1, 2, 250 });
	}

	static readonly ApiTransport Transport = new(new HttpClient(), new ApiClientOptions());

	static readonly ApiOperation Get = new(HttpMethod.Get, "api/students/{studentId}/levels");

	[Fact]
	public void Substitutes_path_values_in_order_whatever_the_placeholders_are_called()
	{
		using var request = Transport.CreateRequest(Get, ["a b/c"]);

		Assert.Equal("api/students/a%20b%2Fc/levels", request.RequestUri!.OriginalString);
	}

	[Fact]
	public void Refuses_the_wrong_number_of_path_values()
	{
		Assert.Throws<ArgumentException>(() => Transport.CreateRequest(Get, []));
		Assert.Throws<ArgumentException>(() => Transport.CreateRequest(Get, ["1", "2"]));
	}

	[Theory]
	[InlineData("")]
	[InlineData(".")]
	[InlineData("..")]
	public void Refuses_a_path_value_that_would_change_the_resource(string value)
	{
		var exception = Assert.Throws<ArgumentException>(() => Transport.CreateRequest(Get, [value]));

		Assert.Contains("path value", exception.Message);
	}

	[Fact]
	public void Writes_query_parameters_and_skips_null_ones()
	{
		var query = new SampleQuery { Criteria = "{\"a\":1}", Tags = ["x", "y"] };

		using var request = Transport.CreateRequest(Get, ["7"], query);

		Assert.Equal("api/students/7/levels?criteria=%7B%22a%22%3A1%7D&tag=x&tag=y", request.RequestUri!.OriginalString);
	}

	[Fact]
	public void Sends_header_parameters_as_headers()
	{
		using var request = Transport.CreateRequest(Get, ["7"], new SampleQuery { Trace = "abc" });

		Assert.Equal("abc", Assert.Single(request.Headers.GetValues("X-Trace")));
	}

	[Theory]
	[InlineData("Bad Header", "v")]
	[InlineData("Content-Language", "en")]
	[InlineData("X-Trace", "a\r\nInjected: b")]
	public void Refuses_a_header_the_request_cannot_carry(string name, string value)
	{
		var exception = Assert.Throws<ArgumentException>(() => Transport.CreateRequest(Get, ["7"], new HeaderQuery(name, value)));

		Assert.Contains(name, exception.Message);
	}

	sealed class ListHeaderQuery : IApiQuery
	{
		public void Apply(ApiRequestParameters parameters) => parameters.Header("X-Tags", new List<string> { "a", "b" });
	}

	[Fact]
	public void Sends_a_list_header_as_comma_separated_values()
	{
		using var request = Transport.CreateRequest(Get, ["7"], new ListHeaderQuery());

		Assert.Equal("a,b", string.Join(",", request.Headers.GetValues("X-Tags")));
	}

	[Fact]
	public void Sends_a_content_header_parameter_on_the_body()
	{
		var put = new ApiOperation(HttpMethod.Put, "api/widgets/{id}");

		using var request = Transport.CreateRequest(put, ["9"], new HeaderQuery("Content-Language", "en"), new Dictionary<string, object?> { ["name"] = "n" });

		Assert.Equal("en", Assert.Single(request.Content!.Headers.ContentLanguage));
	}

	[Theory]
	[InlineData("api/widgets?kind=a")]
	[InlineData("api/widgets#top")]
	public void Refuses_a_template_carrying_a_query_or_fragment(string template)
	{
		var exception = Assert.Throws<ArgumentException>(() => Transport.CreateRequest(new ApiOperation(HttpMethod.Get, template), []));

		Assert.Contains("query or fragment", exception.Message);
	}

	[Fact]
	public void Sends_a_byte_array_as_one_base64_value()
	{
		using var request = Transport.CreateRequest(Get, ["7"], new BytesQuery());

		Assert.Equal("api/students/7/levels?data=AQL6", request.RequestUri!.OriginalString);
		Assert.Equal("AQL6", ApiRequestParameters.Format(new byte[] { 1, 2, 250 }));
	}

	[Fact]
	public void Formats_query_values_with_the_invariant_culture()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("de-DE");

		try
		{
			Assert.Equal("1.5", ApiRequestParameters.Format(1.5m));
			Assert.Equal("2026-09-29", ApiRequestParameters.Format(new DateOnly(2026, 9, 29)));
			Assert.Equal("true", ApiRequestParameters.Format(true));
			Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", ApiRequestParameters.Format(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")));
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[Fact]
	public async Task Sets_the_accept_and_content_type_the_operation_declares()
	{
		var put = new ApiOperation(HttpMethod.Put, "api/widgets/{id}")
		{
			Accept = "application/vnd.example.v2+json, application/vnd.example.errors.v1+json",
			ContentType = "application/vnd.example.v2.1+json"
		};

		using var request = Transport.CreateRequest(put, ["9"], body: new Dictionary<string, object?> { ["name"] = "n", ["gone"] = null });

		Assert.Equal(
			new[] { "application/vnd.example.v2+json", "application/vnd.example.errors.v1+json" },
			request.Headers.GetValues("Accept"));
		Assert.Equal("application/vnd.example.v2.1+json", request.Content!.Headers.ContentType!.ToString());
		Assert.Equal("""{"name":"n","gone":null}""", await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Builds_no_query_object_when_the_caller_configures_none()
	{
		Assert.Null(ApiQuery.Build<SampleQuery>(null));
		Assert.Equal("c", ApiQuery.Build<SampleQuery>(q => q.Criteria = "c")!.Criteria);
	}
}
