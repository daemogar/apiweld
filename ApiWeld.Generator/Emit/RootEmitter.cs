using ApiWeld.Generator.Model;

namespace ApiWeld.Generator.Emit;

/// <summary>Writes the generated half of the client and its navigation root.</summary>
internal static class RootEmitter
{
	public static GeneratedFile Emit(PathNode root, Manifest manifest)
	{
		var client = manifest.Client;
		var api = ReservedNames.Root(client);
		var writer = new CodeWriter(manifest.Namespace);

		writer.Line();
		writer.Summary($"The generated half of {client}; its constructor is declared in the hand-written half.");
		writer.Open($"public partial class {client} : ApiClient");
		writer.Line($"{api}? generatedApi;");
		writer.Line();
		writer.Summary("The generated navigation root.");
		writer.Line($"internal {api} Api => generatedApi ??= new(Transport);");
		writer.Close();

		writer.Line();
		writer.Summary($"The navigation root of {client}.");
		writer.Open($"public sealed class {api}");
		writer.Line("readonly ApiTransport transport;");
		writer.Line();
		writer.Summary("A navigation root over a client's transport.");
		writer.Open($"public {api}(ApiTransport transport)");
		writer.Line("this.transport = transport;");
		writer.Close();
		PathEmitter.Children(writer, root, isRoot: true);
		writer.Close();

		return new($"{client}.g.cs", writer.ToString());
	}
}
