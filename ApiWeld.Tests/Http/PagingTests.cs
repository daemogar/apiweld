using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class PagingTests
{
	sealed class Row
	{
		[JsonPropertyName("id")] public int? Id { get; set; }
	}

	sealed class RowQuery : IApiQuery
	{
		public string? Criteria { get; set; }

		void IApiQuery.Apply(ApiRequestParameters parameters) => parameters.Query("criteria", Criteria);
	}

	static readonly ApiOperation List = new(HttpMethod.Get, "api/rows") { Paging = new("offset", "limit", "X-Total-Count") };

	/// <summary>Serves <paramref name="rows"/> rows, <paramref name="pageSize"/> at a time unless a limit is sent.</summary>
	static FakeHandler Server(int rows, int pageSize = 2, int? total = null, bool ignoreOffset = false, Func<int, int>? idOf = null) => new((request, _) =>
	{
		var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
		var offset = ignoreOffset ? 0 : int.Parse(query["offset"] ?? "0");
		var limit = int.TryParse(query["limit"], out var sent) ? sent : pageSize;
		var ids = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, rows - offset))).Select(row => new { id = idOf?.Invoke(row) ?? row });
		var response = FakeHandler.Json(JsonSerializer.Serialize(ids));

		if (total is { } count)
			response.Headers.Add("X-Total-Count", count.ToString());

		return response;
	});

	static ApiTransport Transport(FakeHandler handler) => new(handler.Client(), new ApiClientOptions());

	static List<string> Offsets(FakeHandler handler)
		=> handler.Requests.Select(r => HttpUtility.ParseQueryString(r.Request.RequestUri!.Query)["offset"]!).ToList();

	[Fact]
	public async Task Walks_every_page_until_the_total()
	{
		var handler = Server(rows: 5, total: 5);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, rows.Select(r => r.Id!.Value));
		Assert.Equal(new[] { "0", "2", "4" }, Offsets(handler));
	}

	[Fact]
	public async Task Returns_the_one_page_when_there_is_no_total_header()
	{
		var handler = Server(rows: 5);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(2, rows.Count);
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Stops_on_an_empty_page_even_when_the_total_says_more()
	{
		var handler = Server(rows: 3, total: 10);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(3, rows.Count);
		Assert.Equal(new[] { "0", "2", "3" }, Offsets(handler));
	}

	[Fact]
	public async Task Throws_when_the_server_ignores_the_offset()
	{
		var handler = Server(rows: 10, total: 10, ignoreOffset: true);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken));

		Assert.Contains("offset 2", exception.Message);
	}

	// Rows 0-3 are identical, so the pages at offsets 0 and 2 are too; the server honors the offset.
	[Fact]
	public async Task Walks_on_past_two_pages_that_really_are_identical()
	{
		var handler = Server(rows: 5, total: 5, idOf: row => row < 4 ? 7 : 8);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 7, 7, 7, 7, 8 }, rows.Select(r => r.Id!.Value));
	}

	[Fact]
	public async Task Fetches_the_next_page_by_hand_past_an_identical_one()
	{
		var handler = Server(rows: 5, total: 5, idOf: row => row < 4 ? 7 : 8);

		var page = await Transport(handler).GetPageAsync<Row>(List, [], null, offset: 0, limit: 2, cancellationToken: TestContext.Current.CancellationToken);
		var next = await page.NextAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 7, 7 }, next!.Items.Select(r => r.Id!.Value));
		Assert.Equal(2, next.Offset);
	}

	[Fact]
	public async Task Throws_from_the_next_page_by_hand_when_the_server_ignores_the_offset()
	{
		var handler = Server(rows: 10, total: 10, ignoreOffset: true);

		var page = await Transport(handler).GetPageAsync<Row>(List, [], null, offset: 0, limit: 2, cancellationToken: TestContext.Current.CancellationToken);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => page.NextAsync(TestContext.Current.CancellationToken));

		Assert.Contains("offset 2", exception.Message);
	}

	[Fact]
	public async Task Sends_the_callers_query_with_every_page()
	{
		var handler = Server(rows: 3, total: 3);

		await Transport(handler).GetAllAsync<Row>(List, [], new RowQuery { Criteria = "c" }, TestContext.Current.CancellationToken);

		Assert.All(handler.Requests, r => Assert.Contains("criteria=c", r.Request.RequestUri!.Query));
	}

	[Fact]
	public async Task Enumerating_stops_fetching_when_the_caller_stops()
	{
		var handler = Server(rows: 10, total: 10);

		await foreach (var row in Transport(handler).EnumerateAsync<Row>(List, [], null, TestContext.Current.CancellationToken))
			if (row.Id == 1)
				break;

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Gets_one_page_and_the_next_by_hand()
	{
		var handler = Server(rows: 5, total: 5);

		var page = await Transport(handler).GetPageAsync<Row>(List, [], null, offset: 2, limit: 2, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 2, 3 }, page.Items.Select(r => r.Id!.Value));
		Assert.Equal(2, page.Offset);
		Assert.Equal(2, page.Limit);
		Assert.Equal(5, page.Total);
		Assert.True(page.HasMore);

		var next = await page.NextAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 4 }, next!.Items.Select(r => r.Id!.Value));
		Assert.False(next.HasMore);
		Assert.Null(await next.NextAsync(TestContext.Current.CancellationToken));
		Assert.Contains("limit=2", handler.Requests[1].Request.RequestUri!.Query);
	}

	[Fact]
	public async Task Has_no_next_page_without_a_total()
	{
		var page = await Transport(Server(rows: 5)).GetPageAsync<Row>(List, [], null, offset: 0, limit: null, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Null(page.Total);
		Assert.False(page.HasMore);
		Assert.Null(await page.NextAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Refuses_to_page_an_operation_that_is_not_paged()
	{
		var unpaged = List with { Paging = null };

		await Assert.ThrowsAsync<InvalidOperationException>(() => Transport(Server(rows: 1)).GetAllAsync<Row>(unpaged, [], null, TestContext.Current.CancellationToken));
	}
}
