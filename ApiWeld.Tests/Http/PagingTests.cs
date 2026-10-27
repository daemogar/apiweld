using System.Net;
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

		public int? Limit { get; set; }

		void IApiQuery.Apply(ApiRequestParameters parameters)
		{
			parameters.Query("criteria", Criteria);
			parameters.Query("limit", Limit);
		}
	}

	/// <summary>A paged operation whose described default limit is <paramref name="limit"/>.</summary>
	static ApiOperation Paged(int limit, string? offsetName = "offset", string? limitName = "limit")
		=> new(HttpMethod.Get, "api/rows") { Paging = new(offsetName, limitName, "X-Total-Count") { DefaultLimit = limit } };

	static readonly ApiOperation List = Paged(2);

	static readonly ApiOperation Fours = Paged(4);

	/// <summary>Serves <paramref name="rows"/> rows, <paramref name="pageSize"/> at a time unless a limit is sent; a window holding a row <paramref name="bad"/> answers 500.</summary>
	static FakeHandler Server(int rows, int pageSize = 2, int? total = null, bool ignoreOffset = false, bool ignoreLimit = false, Func<int, int>? idOf = null, Func<int, bool>? bad = null) => new((request, _) =>
	{
		var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
		var offset = ignoreOffset ? 0 : int.Parse(query["offset"] ?? "0");
		var limit = ignoreLimit ? rows : int.TryParse(query["limit"], out var sent) ? sent : pageSize;
		var window = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, rows - offset))).ToList();

		if (bad is not null && window.Exists(row => bad(row)))
			return FakeHandler.Json("""{"message":"a record in this window cannot be read"}""", status: HttpStatusCode.InternalServerError);

		var ids = window.Select(row => new { id = idOf?.Invoke(row) ?? row });
		var response = FakeHandler.Json(JsonSerializer.Serialize(ids));

		if (total is { } count)
			response.Headers.Add("X-Total-Count", count.ToString());

		return response;
	});

	static ApiTransport Transport(FakeHandler handler) => new(handler.Client(), new ApiClientOptions());

	static ApiTransport Transport(FakeHandler handler, int narrowing) => new(handler.Client(), new ApiClientOptions { PageNarrowingRequests = narrowing });

	static List<string> Offsets(FakeHandler handler) => Sent(handler, "offset");

	static List<string> Sent(FakeHandler handler, string name)
		=> handler.Requests.Select(r => HttpUtility.ParseQueryString(r.Request.RequestUri!.Query)[name]!).ToList();

	static List<int> Ids(IEnumerable<Row> rows) => rows.Select(row => row.Id!.Value).ToList();

	[Fact]
	public async Task Walks_every_page_until_the_total()
	{
		var handler = Server(rows: 5, total: 5);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, rows.Select(r => r.Id!.Value));
		Assert.Equal(new[] { "0", "2", "4" }, Offsets(handler));
	}

	[Fact]
	public async Task Walks_without_a_total_until_a_page_comes_back_short()
	{
		var handler = Server(rows: 5);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Ids(rows));
		Assert.Equal(new[] { "0", "2", "4" }, Offsets(handler));
	}

	[Fact]
	public async Task Ends_on_an_empty_page_when_the_rows_are_an_exact_multiple_of_the_limit()
	{
		var handler = Server(rows: 4);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3 }, Ids(rows));
		Assert.Equal(new[] { "0", "2", "4" }, Offsets(handler));
	}

	[Fact]
	public async Task Takes_a_page_longer_than_the_sent_limit_as_the_whole_answer()
	{
		var handler = Server(rows: 5, ignoreLimit: true);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], new RowQuery { Limit = 2 }, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Ids(rows));
		Assert.Single(handler.Requests);
	}

	// The total says 10, but a page longer than the limit means the server does not page at all.
	[Fact]
	public async Task Takes_a_page_longer_than_the_sent_limit_as_the_whole_answer_even_with_a_total()
	{
		var handler = Server(rows: 5, total: 10, ignoreLimit: true);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], new RowQuery { Limit = 2 }, TestContext.Current.CancellationToken);

		Assert.Equal(5, rows.Count);
		Assert.Single(handler.Requests);
	}

	// No limit was sent, so a longer page ignored nothing: it shows the server's page size is larger.
	[Fact]
	public async Task Walks_at_the_servers_larger_page_size_when_no_limit_was_sent()
	{
		var handler = Server(rows: 7, pageSize: 3);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(7, rows.Count);
		Assert.Equal(new[] { "0", "3", "6" }, Offsets(handler));
	}

	[Fact]
	public async Task Walks_by_the_described_default_without_sending_it()
	{
		var handler = Server(rows: 7, pageSize: 3);

		await Transport(handler).GetAllAsync<Row>(Paged(3), [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "0", "3", "6" }, Offsets(handler));
		Assert.All(handler.Requests, r => Assert.DoesNotContain("limit=", r.Request.RequestUri!.Query));
	}

	[Fact]
	public async Task Walks_by_100_when_the_description_gives_no_default()
	{
		var handler = Server(rows: 150, pageSize: 100);
		var operation = new ApiOperation(HttpMethod.Get, "api/rows") { Paging = new("offset", "limit", "X-Total-Count") };

		var rows = await Transport(handler).GetAllAsync<Row>(operation, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(150, rows.Count);
		Assert.Equal(new[] { "0", "100" }, Offsets(handler));
	}

	[Fact]
	public async Task Holds_the_callers_limit_to_the_described_maximum()
	{
		var handler = Server(rows: 7);
		var operation = Paged(2) with { Paging = Paged(2).Paging! with { MaximumLimit = 3 } };

		var rows = await Transport(handler).GetAllAsync<Row>(operation, [], new RowQuery { Limit = 10 }, TestContext.Current.CancellationToken);

		Assert.Equal(7, rows.Count);
		Assert.Equal(new[] { "3", "3", "3" }, Sent(handler, "limit"));
	}

	[Fact]
	public async Task Walks_by_the_described_maximum_when_it_is_below_the_default()
	{
		var handler = Server(rows: 7, pageSize: 3);
		var operation = new ApiOperation(HttpMethod.Get, "api/rows") { Paging = new("offset", "limit", "X-Total-Count") { MaximumLimit = 3 } };

		await Transport(handler).GetAllAsync<Row>(operation, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "0", "3", "6" }, Offsets(handler));
	}

	// The description declares neither parameter, so a server that ignores both repeats its one page; that page was everything.
	[Fact]
	public async Task Ends_without_throwing_when_an_undeclared_offset_is_ignored()
	{
		var handler = Server(rows: 2, ignoreOffset: true, ignoreLimit: true);
		var operation = List with { Paging = List.Paging! with { Undeclared = true } };

		var rows = await Transport(handler).GetAllAsync<Row>(operation, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1 }, Ids(rows));
		Assert.Equal(new[] { "0", "2", "3" }, Offsets(handler));
	}

	// Without a limit, a half-window cannot be asked for, so the failed page is reported whole at once.
	[Fact]
	public async Task Reports_a_failed_page_as_a_range_when_there_is_no_limit_to_narrow_with()
	{
		var handler = Server(rows: 6, bad: row => row == 3);

		var exception = await Assert.ThrowsAsync<ApiPagingException<Row>>(() => Transport(handler).GetAllAsync<Row>(Paged(2, limitName: null), [], null, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { 0, 1, 4, 5 }, Ids(exception.Rows));
		Assert.Equal("records 2-3", Assert.Single(exception.Failures).ToString());
		Assert.Equal(new[] { "0", "2", "4", "6" }, Offsets(handler));
	}

	[Fact]
	public async Task Walks_by_the_callers_limit_over_the_described_one()
	{
		var handler = Server(rows: 5);

		var rows = await Transport(handler).GetAllAsync<Row>(List, [], new RowQuery { Limit = 3 }, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Ids(rows));
		Assert.Equal(new[] { "3", "3" }, Sent(handler, "limit"));
	}

	// With no limit parameter, the first page's size is the server's page size.
	[Fact]
	public async Task Walks_by_offset_alone_at_the_servers_page_size()
	{
		var handler = Server(rows: 5);

		var rows = await Transport(handler).GetAllAsync<Row>(Paged(100, limitName: null), [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Ids(rows));
		Assert.Equal(new[] { "0", "2", "4" }, Offsets(handler));
		Assert.All(handler.Requests, r => Assert.DoesNotContain("limit=", r.Request.RequestUri!.Query));
	}

	// With no offset parameter, each page asks for every row so far plus one more page.
	[Fact]
	public async Task Walks_by_limit_alone_by_asking_for_more_rows_each_time()
	{
		var handler = Server(rows: 5);

		var rows = await Transport(handler).GetAllAsync<Row>(Paged(2, offsetName: null), [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Ids(rows));
		Assert.Equal(new[] { "2", "4", "6" }, Sent(handler, "limit"));
		Assert.All(handler.Requests, r => Assert.DoesNotContain("offset=", r.Request.RequestUri!.Query));
	}

	// Row 9 is in the window 8-11; with no total, the walk learns the end from the empty page at 12.
	[Fact]
	public async Task Narrows_a_bad_record_without_a_total()
	{
		var handler = Server(rows: 12, pageSize: 4, bad: row => row == 9);

		var exception = await Assert.ThrowsAsync<ApiPagingException<Row>>(() => Transport(handler).GetAllAsync<Row>(Paged(4), [], null, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 }, Ids(exception.Rows));
		Assert.Equal("record 9", Assert.Single(exception.Failures).ToString());
		Assert.Equal(new[] { "0", "4", "8", "8", "10", "8", "9", "12" }, Offsets(handler));
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
	public async Task Pages_by_hand_without_a_total_until_a_page_comes_back_short()
	{
		var handler = Server(rows: 3);

		var page = await Transport(handler).GetPageAsync<Row>(List, [], null, offset: 0, limit: null, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Null(page.Total);
		Assert.Null(page.Limit);
		Assert.True(page.HasMore);

		var next = await page.NextAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 2 }, Ids(next!.Items));
		Assert.False(next.HasMore);
		Assert.Null(await next.NextAsync(TestContext.Current.CancellationToken));
		Assert.Equal(2, handler.Requests.Count);
	}

	[Fact]
	public async Task Has_no_next_page_by_hand_when_a_page_is_longer_than_the_sent_limit()
	{
		var page = await Transport(Server(rows: 5, ignoreLimit: true)).GetPageAsync<Row>(List, [], null, offset: 0, limit: 2, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(5, page.Items.Count);
		Assert.False(page.HasMore);
	}

	[Fact]
	public async Task Refuses_to_page_an_operation_that_is_not_paged()
	{
		var unpaged = List with { Paging = null };

		await Assert.ThrowsAsync<InvalidOperationException>(() => Transport(Server(rows: 1)).GetAllAsync<Row>(unpaged, [], null, TestContext.Current.CancellationToken));
	}

	// Rows 8-11 are one page; only row 9 is bad.
	[Fact]
	public async Task Returns_every_other_record_and_names_the_bad_one()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12, bad: row => row == 9);

		var exception = await Assert.ThrowsAsync<ApiPagingException<Row>>(() => Transport(handler).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 }, Ids(exception.Rows));

		var failure = Assert.Single(exception.Failures);

		Assert.Equal(9, failure.Offset);
		Assert.True(failure.IsRecord);
		Assert.Contains("record 9", exception.Message);
		Assert.IsAssignableFrom<ApiResponseException>(failure.Error);
	}

	// The walk sends no limit the caller did not set, so a client's page size stays the server's business.
	[Fact]
	public async Task Spends_no_extra_requests_while_every_page_answers()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12);

		await Transport(handler).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "0", "4", "8" }, Offsets(handler));
		Assert.All(handler.Requests, r => Assert.DoesNotContain("limit=", r.Request.RequestUri!.Query));
	}

	[Fact]
	public async Task Narrows_one_bad_record_in_four_extra_requests()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12, bad: row => row == 9);

		await Assert.ThrowsAsync<ApiPagingException<Row>>(() => Transport(handler).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken));

		// Three pages, then the halves of the failed window: 8-9 and 10-11, then 8 and 9.
		Assert.Equal(new[] { "0", "4", "8", "8", "10", "8", "9" }, Offsets(handler));
	}

	[Fact]
	public async Task Reports_the_range_when_the_whole_page_is_bad()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12, bad: row => row >= 8);

		var exception = await Assert.ThrowsAsync<ApiPagingException<Row>>(() => Transport(handler).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Ids(exception.Rows));

		var failure = Assert.Single(exception.Failures);

		Assert.Equal(8, failure.Offset);
		Assert.Equal(4, failure.Count);
		Assert.False(failure.IsRecord);
		Assert.Contains("records 8-11", exception.Message);

		// Both halves failed, so neither is split any further.
		Assert.Equal(new[] { "0", "4", "8", "8", "10" }, Offsets(handler));
	}

	[Fact]
	public async Task Reports_the_range_when_the_budget_runs_out()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12, bad: row => row == 9);

		var exception = await Assert.ThrowsAsync<ApiPagingException<Row>>(() => Transport(handler, narrowing: 2).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 10, 11 }, Ids(exception.Rows));
		Assert.Equal("records 8-9", Assert.Single(exception.Failures).ToString());
	}

	[Fact]
	public async Task Throws_the_response_itself_when_narrowing_is_off()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12, bad: row => row == 9);

		await Assert.ThrowsAnyAsync<ApiResponseException>(() => Transport(handler, narrowing: 0).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken));

		Assert.Equal(3, handler.Requests.Count);
	}

	// Nothing has been fetched yet, so neither the page size nor the total is known and there is no window to halve.
	[Fact]
	public async Task Throws_the_response_itself_when_the_first_page_fails()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12, bad: row => row == 1);

		await Assert.ThrowsAnyAsync<ApiResponseException>(() => Transport(handler).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken));

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Enumerating_yields_the_other_records_and_then_throws()
	{
		var handler = Server(rows: 12, pageSize: 4, total: 12, bad: row => row == 9);
		var rows = new List<int>();

		var exception = await Assert.ThrowsAsync<ApiPagingException>(async () =>
		{
			await foreach (var row in Transport(handler).EnumerateAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken))
				rows.Add(row.Id!.Value);
		});

		Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 }, rows);
		Assert.Equal("record 9", Assert.Single(exception.Failures).ToString());
	}

	// A walk that meets a bad record in two different pages reports both and returns the rest.
	[Fact]
	public async Task Names_every_bad_record_it_met()
	{
		var handler = Server(rows: 16, pageSize: 4, total: 16, bad: row => row is 5 or 14);

		var exception = await Assert.ThrowsAsync<ApiPagingException<Row>>(() => Transport(handler).GetAllAsync<Row>(Fours, [], null, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { 0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 15 }, Ids(exception.Rows));
		Assert.Equal(new[] { "record 5", "record 14" }, exception.Failures.Select(failure => failure.ToString()));
	}
}
