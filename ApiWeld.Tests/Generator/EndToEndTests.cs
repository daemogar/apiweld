using System.Net;
using System.Text.Json.Nodes;
using System.Web;

using ApiWeld.Generator;
using ApiWeld.Tests.Http;

namespace ApiWeld.Tests.Generator;

public class EndToEndTests
{
	const string Driver = """
		using System;
		using System.Net.Http;
		using System.Threading.Tasks;

		namespace Example;

		/// <summary>Exercises every kind of generated operation.</summary>
		public static class Driver
		{
			/// <summary>Runs the calls against a client and summarizes the results.</summary>
			/// <param name="http">The client to send through.</param>
			/// <returns>The summary.</returns>
			public static async Task<string> RunAsync(HttpClient http)
			{
				var api = new ExampleClient(http).Paths;
				var all = await api.Widgets.V2.GetAsync(query => query.Criteria = "{}");
				var page = await api.Widgets.V2.GetPagedAsync(0, 2);
				var streamed = 0;

				await foreach (var widget in api.Widgets.V2.EnumerateAsync())
					streamed++;

				var created = await api.Widgets.V2.PostAsync(new WidgetV2Request { Name = "new", Status = WidgetV2StatusRequest.Active });
				var parts = await api.Widgets["w-1"].Parts.V1.GetAsync();
				var gadgets = await api.Gadgets.V0.GetAsync();
				await api.Widgets["w-1"].V2.DeleteAsync();

				return string.Join("|", all.Count, page.Items.Count, page.Total, streamed, created?.Name, created?.Status, parts.Count, gadgets.Count);
			}
		}
		""";

	const string V2 = "application/vnd.example.v2+json";

	static FakeHandler Server() => new((request, body) =>
	{
		var path = request.RequestUri!.AbsolutePath;
		var query = HttpUtility.ParseQueryString(request.RequestUri.Query);

		if (request.Method == HttpMethod.Get && path == "/api/widgets")
		{
			var offset = int.Parse(query["offset"] ?? "0");
			var limit = int.TryParse(query["limit"], out var sent) ? sent : 2;
			var rows = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, 3 - offset)))
				.Select(i => $$"""{ "id": "00000000-0000-0000-0000-00000000000{{i}}", "name": "w{{i}}", "status": "active" }""");
			var response = FakeHandler.Json("[" + string.Join(",", rows) + "]", V2);
			response.Headers.Add("X-Total-Count", "3");

			return response;
		}

		if (request.Method == HttpMethod.Post && path == "/api/widgets")
		{
			var name = JsonNode.Parse(body!)!["name"]!.GetValue<string>();

			return FakeHandler.Json($$"""{ "name": "{{name}}", "status": "active" }""", V2, HttpStatusCode.Created);
		}

		if (request.Method == HttpMethod.Get && path == "/api/widgets/w-1/parts")
			return FakeHandler.Json("""[ { "sku": "a", "quantity": 1 }, { "sku": "b", "quantity": "2" } ]""", "application/vnd.example.v1+json");

		if (request.Method == HttpMethod.Get && path == "/api/gadgets")
			return FakeHandler.Json("""[ { "code": "g" } ]""");

		if (request.Method == HttpMethod.Delete && path == "/api/widgets/w-1")
			return new HttpResponseMessage(HttpStatusCode.NoContent);

		return new HttpResponseMessage(HttpStatusCode.NotFound);
	});

	[Fact]
	public async Task A_generated_client_drives_every_kind_of_operation()
	{
		var result = ClientGenerator.Generate(EmissionTests.FixtureManifest, EmissionTests.Fixtures());
		var (assembly, diagnostics) = Compiler.Compile(result.Files
			.Select(file => (file.Path, file.Content))
			.Append(("Consumer.cs", EmissionTests.Consumer))
			.Append(("Driver.cs", Driver)));

		Assert.Empty(diagnostics);

		var server = Server();
		var run = (Task<string>)assembly!.GetType("Example.Driver")!.GetMethod("RunAsync")!.Invoke(null, [server.Client()])!;

		Assert.Equal("3|2|3|3|new|active|2|1", await run);

		var list = server.Requests.First(r => r.Request.Method == HttpMethod.Get && r.Request.RequestUri!.AbsolutePath == "/api/widgets").Request;
		Assert.Equal("application/vnd.example.v2+json, application/vnd.example.errors.v1+json", string.Join(", ", list.Headers.Accept));
		Assert.Contains("criteria=%7B%7D", list.RequestUri!.Query);

		var delete = server.Requests.Single(r => r.Request.Method == HttpMethod.Delete).Request;
		Assert.Equal("application/vnd.example.v2+json", string.Join(", ", delete.Headers.Accept));
	}
}
