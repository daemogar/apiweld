using ApiWeld.Generator.Model;

namespace ApiWeld.Generator.Emit;

/// <summary>Writes one path node: its navigation class, one class per version, and their query classes.</summary>
internal static class PathEmitter
{
	public static GeneratedFile Emit(PathNode node, Manifest manifest)
	{
		var writer = new CodeWriter(manifest.Namespace);
		Node(writer, node);

		foreach (var (member, operations) in node.Versions)
		{
			var ordered = operations.OrderBy(operation => operation.Method, StringComparer.Ordinal).ToList();
			Operations(writer, node, member, ordered, manifest.Paging);

			foreach (var operation in ordered.Where(operation => operation.Parameters.Count > 0))
				Query(writer, node, member, operation);
		}

		return new($"Paths/{node.ClassBase}.g.cs", writer.ToString());
	}

	/// <summary>Properties and indexers leading from <paramref name="node"/> to its children.</summary>
	public static void Children(CodeWriter writer, PathNode node, bool isRoot)
	{
		var values = isRoot ? "[]" : "path";
		var prefix = isRoot ? "" : ".. path, ";

		foreach (var child in node.Children.Values)
		{
			writer.Line();

			if (!child.IsParameter)
			{
				writer.Summary($"The path {child.Display}.");
				writer.Line($"public {child.ClassBase}Node {Words.Identifier(Words.Pascal(child.Segment))} => new(transport, {values});");
				continue;
			}

			// The indexer parameter must not hide the node's own fields.
			var name = Words.Unique(Words.Identifier(Words.Camel(child.ParameterName), "value"), new HashSet<string>(StringComparer.Ordinal) { "path", "transport" });

			if (child.ParameterType == "Guid")
			{
				writer.Summary($"The item at {child.Display}.");
				writer.Line($"public {child.ClassBase}Node this[Guid {name}] => new(transport, [{prefix}{name}.ToString(\"D\")]);");
				writer.Line();
			}

			writer.Summary($"The item at {child.Display}.");
			writer.Line($"public {child.ClassBase}Node this[string {name}] => new(transport, [{prefix}{name}]);");
		}
	}

	static void Node(CodeWriter writer, PathNode node)
	{
		var type = node.ClassBase + "Node";

		writer.Line();
		writer.Summary($"The path {node.Display}.");
		writer.Open($"public sealed class {type}");
		Fields(writer, type);
		Children(writer, node, isRoot: false);

		foreach (var (member, operations) in node.Versions)
		{
			writer.Line();
			writer.Summary(VersionSummary(operations[0]));
			writer.Line($"public {node.ClassBase}{member}Operations {member} => new(transport, path);");
		}

		writer.Close();
	}

	static void Fields(CodeWriter writer, string type)
	{
		writer.Line("readonly ApiTransport transport;");
		writer.Line("readonly string[] path;");
		writer.Line();
		writer.Open($"internal {type}(ApiTransport transport, string[] path)");
		writer.Line("this.transport = transport;");
		writer.Line("this.path = path;");
		writer.Close();
	}

	static string VersionSummary(OperationModel operation) => operation.Version is null
		? $"Unversioned operations; media type {operation.MediaType ?? "none"}."
		: $"Version {operation.Version}; media type {operation.MediaType}.";

	static void Operations(CodeWriter writer, PathNode node, string member, List<OperationModel> operations, PagingConvention paging)
	{
		var type = $"{node.ClassBase}{member}Operations";

		writer.Line();
		writer.Summary($"{VersionSummary(operations[0])} Operations on {node.Display}.");
		writer.Open($"public sealed class {type}");

		foreach (var operation in operations)
			Descriptor(writer, operation, paging);

		Fields(writer, type);

		foreach (var operation in operations)
			Methods(writer, node, member, operation);

		writer.Close();
	}

	static void Descriptor(CodeWriter writer, OperationModel operation, PagingConvention paging)
	{
		writer.Open($"static readonly ApiOperation {operation.Method}Operation = new(HttpMethod.{operation.Method}, {CodeWriter.Literal(operation.Template)})");

		if (operation.Accept is { } accept)
			writer.Line($"Accept = {CodeWriter.Literal(accept)},");

		if (operation.ContentType is { } contentType)
			writer.Line($"ContentType = {CodeWriter.Literal(contentType)},");

		if (operation.Version is { } version)
			writer.Line($"Version = {CodeWriter.Literal(version)},");

		if (operation.IsPaged)
			writer.Line($"Paging = new({CodeWriter.Literal(paging.Offset)}, {CodeWriter.Literal(paging.Limit)}, {CodeWriter.Literal(paging.TotalHeader)}),");

		if (operation.Errors.Count > 0)
		{
			writer.Open("Errors = new Dictionary<int, ApiErrorFactory>");

			foreach (var (status, type) in operation.Errors)
				writer.Line($"[{status}] = {(TypeNames.IsClass(type) ? $"ApiResponseException<{TypeNames.Of(type)}>.Create" : "ApiResponseException.Create")},");

			writer.Close(",");
		}

		writer.Close(";");
		writer.Line();
	}

	static void Methods(CodeWriter writer, PathNode node, string member, OperationModel operation)
	{
		var method = operation.Method;
		var verb = method.ToUpperInvariant();
		var descriptor = method + "Operation";
		var where = node.Display;
		var query = operation.Parameters.Count > 0 ? $"{node.ClassBase}{member}{method}Query" : null;
		var body = operation.Request is { } request ? TypeNames.Of(request) : null;
		var bodyParameter = body is null ? "" : $"{body} body, ";
		var bodyArgument = body is null ? "" : "body, ";
		var buildParameters = string.Join(", ", new[] { body is null ? null : $"{body} body", query is null ? null : $"Action<{query}>? query = null" }.OfType<string>());
		var buildArguments = string.Join(", ", new[] { body is null ? null : "body", query is null ? null : "query" }.OfType<string>());

		writer.Line();
		writer.Summary($"The {verb} {where} request, built but not sent.");
		writer.Line($"public HttpRequestMessage Build{method}Request({buildParameters})");
		writer.Line($"\t=> transport.CreateRequest({descriptor}, path, {(query is null ? "null" : "ApiQuery.Build(query)")}, {(body is null ? "null" : "body")});");

		if (operation.IsPaged)
		{
			Paged(writer, operation, descriptor, query!, where);
			return;
		}

		var call = $"Build{method}Request({buildArguments})";
		var (returns, send) = operation.Response switch
		{
			null => ("Task", $"transport.SendAsync({descriptor}, {call}, cancellationToken)"),
			ListRef list => ($"async Task<{TypeNames.Of(list)}>", $"await transport.SendAsync<{TypeNames.Of(list)}>({descriptor}, {call}, cancellationToken) ?? []"),
			var type => ($"Task<{TypeNames.Of(type)}?>", $"transport.SendAsync<{TypeNames.Of(type)}>({descriptor}, {call}, cancellationToken)")
		};

		writer.Line();
		writer.Summary($"Sends {verb} {where}.");

		if (query is null)
		{
			writer.Line($"public {returns} {method}Async({bodyParameter}CancellationToken cancellationToken = default)");
			writer.Line($"\t=> {send};");
			return;
		}

		writer.Line($"public {returns.Replace("async ", "")} {method}Async({bodyParameter}CancellationToken cancellationToken = default)");
		writer.Line($"\t=> {method}Async({bodyArgument}null, cancellationToken);");
		writer.Line();
		writer.Summary($"Sends {verb} {where} with query parameters.");
		writer.Line($"public {returns} {method}Async({bodyParameter}Action<{query}>? query, CancellationToken cancellationToken = default)");
		writer.Line($"\t=> {send};");
	}

	static void Paged(CodeWriter writer, OperationModel operation, string descriptor, string query, string where)
	{
		var item = TypeNames.Of(((ListRef)operation.Response!).Item);

		writer.Line();
		writer.Summary($"Every row of GET {where}, walking all pages.");
		writer.Line($"public Task<List<{item}>> GetAsync(CancellationToken cancellationToken = default) => GetAsync(null, cancellationToken);");
		writer.Line();
		writer.Summary($"Every row of GET {where} matching the query, walking all pages.");
		writer.Line($"public Task<List<{item}>> GetAsync(Action<{query}>? query, CancellationToken cancellationToken = default)");
		writer.Line($"\t=> transport.GetAllAsync<{item}>({descriptor}, path, ApiQuery.Build(query), cancellationToken);");
		writer.Line();
		writer.Summary($"The rows of GET {where}, fetching each page when it is reached.");
		writer.Line($"public IAsyncEnumerable<{item}> EnumerateAsync(CancellationToken cancellationToken = default) => EnumerateAsync(null, cancellationToken);");
		writer.Line();
		writer.Summary($"The rows of GET {where} matching the query, fetching each page when it is reached.");
		writer.Line($"public IAsyncEnumerable<{item}> EnumerateAsync(Action<{query}>? query, CancellationToken cancellationToken = default)");
		writer.Line($"\t=> transport.EnumerateAsync<{item}>({descriptor}, path, ApiQuery.Build(query), cancellationToken);");
		writer.Line();
		writer.Summary($"One page of GET {where}.");
		writer.Line($"public Task<Page<{item}>> GetPagedAsync(int offset, int? limit = null, CancellationToken cancellationToken = default) => GetPagedAsync(offset, limit, null, cancellationToken);");
		writer.Line();
		writer.Summary($"One page of GET {where} matching the query.");
		writer.Line($"public Task<Page<{item}>> GetPagedAsync(int offset, int? limit, Action<{query}>? query, CancellationToken cancellationToken = default)");
		writer.Line($"\t=> transport.GetPageAsync<{item}>({descriptor}, path, ApiQuery.Build(query), offset, limit, cancellationToken);");
	}

	static void Query(CodeWriter writer, PathNode node, string member, OperationModel operation)
	{
		var type = $"{node.ClassBase}{member}{operation.Method}Query";
		var taken = new HashSet<string>(StringComparer.Ordinal) { type };
		var members = new List<(QueryParameter Parameter, string Member)>();

		writer.Line();
		writer.Summary($"Parameters for {operation.Method.ToUpperInvariant()} {node.Display} at {member}.");
		writer.Open($"public sealed class {type} : IApiQuery");

		foreach (var parameter in operation.Parameters)
		{
			var name = Words.Unique(Words.Identifier(Words.Pascal(parameter.Name)), taken);
			var summary = parameter.Description ?? $"The {parameter.Name} {parameter.In} parameter.";

			members.Add((parameter, name));
			writer.Summary(parameter.Required ? summary.TrimEnd().TrimEnd('.') + ". Required." : summary);
			writer.Line($"public {TypeNames.Nullable(parameter.Type)} {name} {{ get; set; }}");
			writer.Line();
		}

		writer.Open("void IApiQuery.Apply(ApiRequestParameters parameters)");

		foreach (var (parameter, name) in members)
			writer.Line($"parameters.{(parameter.In == "header" ? "Header" : "Query")}({CodeWriter.Literal(parameter.Name)}, {name});");

		writer.Close();
		writer.Close();
	}
}
