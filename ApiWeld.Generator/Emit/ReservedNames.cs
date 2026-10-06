using ApiWeld.Generator.Model;

namespace ApiWeld.Generator.Emit;

/// <summary>The type names generated navigation takes, and the check that no model takes one too.</summary>
internal static class ReservedNames
{
	/// <summary>Simple names, at arity zero, that generated code uses from the runtime and the base library.</summary>
	internal static readonly IReadOnlyList<string> Used =
	[
		"ApiClient", "ApiErrorFactory", "ApiOperation", "ApiQuery", "ApiRequestParameters", "ApiResponseException",
		"ApiTransport", "IApiQuery", "CancellationToken", "DateOnly", "DateTimeOffset", "Guid", "HttpMethod",
		"HttpRequestMessage", "JsonConverter", "JsonConverterAttribute", "JsonElement", "JsonExtensionData",
		"JsonExtensionDataAttribute", "JsonPropertyName", "JsonPropertyNameAttribute", "Task"
	];

	public static string Root(string client) => client + "Api";

	public static string Node(PathNode node) => node.ClassBase + "Node";

	public static string Operations(PathNode node, string member) => node.ClassBase + member + "Operations";

	public static string Query(PathNode node, string member, string method) => node.ClassBase + member + method + "Query";

	/// <summary>Reports every model whose name a navigation class or a used type already has.</summary>
	public static void Check(ApiModel model, string client, DiagnosticBag diagnostics)
	{
		var taken = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var name in Used)
			taken.TryAdd(name, "a type the generated code uses");

		taken.TryAdd(Root(client), "the navigation root");

		foreach (var node in model.Root.Descendants())
		{
			var what = $"the navigation class for {node.Display}";
			taken.TryAdd(Node(node), what);

			foreach (var (member, operations) in node.Versions)
			{
				taken.TryAdd(Operations(node, member), what);

				foreach (var operation in operations.Where(operation => operation.Parameters.Count > 0))
					taken.TryAdd(Query(node, member, operation.Method), what);
			}
		}

		foreach (var type in model.Models.Where(type => taken.ContainsKey(type.Name)))
			diagnostics.Error($"{type.Name}: a model would share its name with {taken[type.Name]}; add a \"names\" entry.");
	}
}
