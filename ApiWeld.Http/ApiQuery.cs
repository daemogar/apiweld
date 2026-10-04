namespace ApiWeld.Http;

/// <summary>A generated query object: typed parameters for one operation.</summary>
public interface IApiQuery
{
	/// <summary>Writes this query's values.</summary>
	void Apply(ApiRequestParameters parameters);
}

/// <summary>Helpers generated operations use to build query objects.</summary>
public static class ApiQuery
{
	/// <summary>A new query configured by <paramref name="configure"/>, or null when there is nothing to configure.</summary>
	public static TQuery? Build<TQuery>(Action<TQuery>? configure) where TQuery : class, IApiQuery, new()
	{
		if (configure is null)
			return null;

		var query = new TQuery();
		configure(query);

		return query;
	}
}
