using System.Text.Json.Nodes;

using ApiWeld.Core;
using ApiWeld.Http;

namespace ApiWeld.Generator.Model;

/// <summary>Builds one model from every description: a shared path tree, versioned operations and merged types.</summary>
/// <remarks>See README.md, "Paths and versions".</remarks>
internal sealed class ApiModelBuilder
{
	static readonly string[] Methods = ["get", "put", "post", "delete", "patch"];

	static readonly string[] Unsupported = ["head", "options", "trace"];

	readonly Manifest manifest;
	readonly DiagnosticBag diagnostics;
	readonly ShapeBuilder shapes;
	readonly PathNode root = new("", false, null);

	ApiModelBuilder(Manifest manifest, DiagnosticBag diagnostics)
	{
		this.manifest = manifest;
		this.diagnostics = diagnostics;
		shapes = new ShapeBuilder(diagnostics, manifest.Names);
	}

	public static ApiModel Build(Manifest manifest, IEnumerable<DescriptionSource> sources, DiagnosticBag diagnostics)
	{
		var builder = new ApiModelBuilder(manifest, diagnostics);

		foreach (var source in sources.OrderBy(source => source.FileName, StringComparer.Ordinal))
			builder.Add(source);

		builder.Check();

		return new(builder.root, [.. builder.shapes.Models]);
	}

	void Add(DescriptionSource source)
	{
		var file = source.FileName;
		var stem = Path.GetFileNameWithoutExtension(file);
		JsonObject document;

		try
		{
			document = OpenApiNormalizer.Normalize(source.Text, stem);
		}
		catch (Exception exception)
		{
			diagnostics.Error($"{file}: could not normalize — {exception.Message}");
			return;
		}

		if (document["paths"] is not JsonObject paths)
		{
			diagnostics.Warn($"{file}: declares no paths.");
			return;
		}

		var resource = manifest.Names.TryGetValue(stem, out var renamed) ? renamed : Words.SingularPascal(stem);

		var operations = (
			from path in paths.OrderBy(path => path.Key, StringComparer.Ordinal)
			let item = References.Resolve(path.Value, document).Node
			where item is not null
			from method in Methods
			where item[method] is JsonObject
			select (Path: path.Key, Item: item, Method: method, Operation: (JsonObject)item[method]!)).ToList();

		foreach (var path in paths.OrderBy(path => path.Key, StringComparer.Ordinal))
			foreach (var method in Unsupported.Where(method => References.Resolve(path.Value, document).Node?[method] is JsonObject))
				diagnostics.Warn($"{file}: {method.ToUpperInvariant()} {path.Key} is not supported and is skipped.");

		var versions = operations
			.Select(operation => MediaTypes.Pick(Success(operation.Operation, document)?["content"]))
			.Select(picked => (Version: MediaTypeVersion.Read(picked?.MediaType), MediaType: picked?.MediaType))
			.Where(pair => pair.Version is not null)
			.Select(pair => (Version: pair.Version!, MediaType: pair.MediaType!))
			.DistinctBy(pair => pair.Version)
			.ToList();

		(string Version, string MediaType)? inherited = versions.Count == 1 ? versions[0] : null;

		if (JsonText.String(document["info"], "version") is { } declared && versions.Count > 0
			&& versions.All(pair => pair.Version != MediaTypeVersion.Normalize(declared)))
			diagnostics.Warn($"{file}: info.version says {MediaTypeVersion.Normalize(declared)} but its media types say {string.Join(", ", versions.Select(pair => pair.Version))}; the media types are used.");

		foreach (var (path, item, method, operation) in operations)
			AddOperation(file, document, resource, inherited, path, item, method, operation);
	}

	void AddOperation(string file, JsonObject document, string resource, (string Version, string MediaType)? inherited,
		string path, JsonObject item, string method, JsonObject operation)
	{
		var verb = Words.Pascal(method);
		var label = $"{method.ToUpperInvariant()} {path}";
		var success = Success(operation, document);
		var picked = MediaTypes.Pick(success?["content"]);

		if (picked is null && success?["content"] is JsonObject { Count: > 0 })
			diagnostics.Warn($"{file}: {label} declares no JSON response; it is generated without a response body.");

		var mediaType = picked?.MediaType ?? inherited?.MediaType;
		var version = MediaTypeVersion.Read(mediaType);
		var member = MediaTypeVersion.MemberName(version);
		var context = new NameContext(resource, member, verb, [], "Response", Direction.Response, method == "get", file);
		var response = picked?.Schema is { } schema ? shapes.Build(schema, document, context) : null;

		var body = MediaTypes.Pick(References.Resolve(operation["requestBody"], document).Node?["content"]);
		var request = body?.Schema is { } requestSchema
			? shapes.Build(requestSchema, document, context with { Suffix = "Request", Direction = Direction.Request })
			: null;

		var (errors, errorTypes) = Errors(file, document, resource, operation);
		var accept = string.Join(", ", new[] { mediaType }.OfType<string>().Concat(errorTypes.Where(type => type != mediaType)));
		var parameters = Parameters(file, label, document, item, operation, out var pathTypes);
		var paged = method == "get" && response is ListRef
			&& parameters.Any(p => p.In == "query" && p.Name == manifest.Paging.Offset)
			&& parameters.Any(p => p.In == "query" && p.Name == manifest.Paging.Limit);

		if (paged)
			parameters.RemoveAll(p => p.In == "query" && p.Name == manifest.Paging.Offset);

		if (Place(file, label, path, pathTypes) is not { } node)
			return;

		var model = new OperationModel
		{
			Method = verb,
			Template = path.TrimStart('/'),
			Version = version,
			VersionMember = member,
			Source = file,
			MediaType = mediaType,
			Accept = accept.Length > 0 ? accept : null,
			ContentType = body?.MediaType,
			Response = response,
			Request = request,
			IsPaged = paged,
			Parameters = parameters,
			Errors = errors
		};

		if (!node.Versions.TryGetValue(member, out var list))
			node.Versions.Add(member, list = []);

		if (list.FirstOrDefault(existing => existing.Method == verb) is { } clash)
		{
			diagnostics.Error($"{method.ToUpperInvariant()} {node.Display} at {member} is defined by both {clash.Source} ({clash.Template}) and {file} ({model.Template}).");
			return;
		}

		list.Add(model);
	}

	static JsonObject? Success(JsonObject operation, JsonObject document)
		=> (operation["responses"] as JsonObject)?
			.Where(response => int.TryParse(response.Key, out var code) && code is >= 200 and < 300)
			.OrderBy(response => response.Key, StringComparer.Ordinal)
			.Select(response => References.Resolve(response.Value, document).Node)
			.FirstOrDefault(response => response is not null);

	(SortedDictionary<int, TypeRef> Types, List<string> MediaTypes) Errors(string file, JsonObject document, string resource, JsonObject operation)
	{
		var types = new SortedDictionary<int, TypeRef>();
		var mediaTypes = new List<string>();

		foreach (var (status, value) in (operation["responses"] as JsonObject ?? new JsonObject()).OrderBy(response => response.Key, StringComparer.Ordinal))
		{
			if (!int.TryParse(status, out var code) || code < 400)
				continue;

			if (MediaTypes.Pick(References.Resolve(value, document).Node?["content"]) is not { } error)
				continue;

			if (!mediaTypes.Contains(error.MediaType))
				mediaTypes.Add(error.MediaType);

			if (error.Schema is null)
				continue;

			var errorVersion = MediaTypeVersion.Read(error.MediaType);
			var context = new NameContext(resource + "Error", errorVersion is null ? "" : MediaTypeVersion.MemberName(errorVersion),
				"", [], "", Direction.Error, false, file, ErrorRoot: true);

			types[code] = shapes.Build(error.Schema, document, context);
		}

		return (types, mediaTypes);
	}

	List<QueryParameter> Parameters(string file, string label, JsonObject document, JsonObject item, JsonObject operation, out Dictionary<string, string> pathTypes)
	{
		var declared = new List<JsonObject>();

		foreach (var list in new[] { item["parameters"], operation["parameters"] })
			foreach (var entry in list as JsonArray ?? new JsonArray())
				if (References.Resolve(entry, document).Node is { } parameter
					&& JsonText.String(parameter, "name") is { } name && JsonText.String(parameter, "in") is { } location)
				{
					var existing = declared.FindIndex(p => JsonText.String(p, "name") == name && JsonText.String(p, "in") == location);

					if (existing >= 0)
						declared[existing] = parameter;
					else
						declared.Add(parameter);
				}

		pathTypes = new Dictionary<string, string>(StringComparer.Ordinal);
		var parameters = new List<QueryParameter>();

		foreach (var parameter in declared)
		{
			var name = JsonText.String(parameter, "name")!;
			var location = JsonText.String(parameter, "in")!;
			var type = ParameterType(parameter, document);
			var description = JsonText.String(parameter, "description");
			var required = JsonText.Flag(parameter, "required");

			switch (location)
			{
				case "path":
					pathTypes[name] = type == ScalarRef.Guid ? "Guid" : "string";
					break;
				case "query":
					parameters.Add(new(name, "query", type, description, required));
					break;
				case "header" when IsAbsorbed(name):
					break;
				case "header":
					parameters.Add(new(name, "header", type, description, required));
					break;
				default:
					diagnostics.Warn($"{file}: {label} parameter {name} in {location} is not supported and is skipped.");
					break;
			}
		}

		return parameters;
	}

	static bool IsAbsorbed(string header) => new string(header.Where(char.IsLetter).ToArray()).ToLowerInvariant() is "accept" or "contenttype";

	static TypeRef ParameterType(JsonObject parameter, JsonObject document)
	{
		var schema = References.Resolve(parameter["schema"], document).Node;

		return JsonText.String(schema, "type") == "array"
			? new ListRef(ScalarOf(References.Resolve(schema!["items"], document).Node))
			: ScalarOf(schema);
	}

	static ScalarRef ScalarOf(JsonObject? schema)
		=> ShapeBuilder.Scalar(JsonText.String(schema, "type"), JsonText.String(schema, "format")) ?? ScalarRef.String;

	PathNode? Place(string file, string label, string path, Dictionary<string, string> pathTypes)
	{
		var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		var skip = manifest.BasePaths
			.Select(basePath => basePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
			.Where(basePath => basePath.Length <= segments.Length && basePath.SequenceEqual(segments.Take(basePath.Length), StringComparer.Ordinal))
			.Select(basePath => basePath.Length)
			.DefaultIfEmpty(0)
			.Max();

		if (segments.Length == skip)
		{
			diagnostics.Error($"{file}: {label} sits at the root of the API once base paths are removed; that is not supported.");
			return null;
		}

		var node = root;

		foreach (var segment in segments.Skip(skip))
		{
			var isParameter = segment.StartsWith('{') && segment.EndsWith('}');
			node = node.Child(segment, isParameter);

			if (!isParameter)
				continue;

			var name = segment[1..^1];
			var type = pathTypes.GetValueOrDefault(name, "string");

			if (node.ParameterName.Length == 0)
			{
				node.ParameterName = name;
				node.ParameterType = type;
			}
			else if (node.ParameterType != type && !node.TypeConflict)
			{
				diagnostics.Warn($"{file}: path parameter {{{name}}} in {path} is {type}, but the same position is {node.ParameterType} elsewhere; its indexer takes string.");
				node.TypeConflict = true;
				node.ParameterType = "string";
			}
		}

		return node;
	}

	/// <summary>Refuses trees whose members or classes would share a name.</summary>
	void Check()
	{
		foreach (var node in root.Descendants().Prepend(root))
		{
			var members = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var child in node.Children.Values.Where(child => !child.IsParameter))
			{
				var member = Words.Identifier(Words.Pascal(child.Segment));

				if (!members.TryAdd(member, child.Display))
					diagnostics.Error($"{child.Display} and {members[member]} would both be named {member}.");
			}

			foreach (var version in node.Versions.Keys.Where(members.ContainsKey))
				diagnostics.Error($"{members[version]} would share its name with the version member {version} of {node.Display}.");
		}

		foreach (var clash in root.Descendants().GroupBy(node => node.ClassBase, StringComparer.Ordinal).Where(group => group.Count() > 1))
			diagnostics.Error($"{string.Join(" and ", clash.Select(node => node.Display))} would both generate {clash.Key}Node.");
	}
}
