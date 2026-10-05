namespace ApiWeld.Tests.Generator;

public class CompilerTests
{
	[Fact]
	public void Reports_warning_wave_diagnostics_a_consumer_build_would_report()
	{
		var (_, diagnostics) = Compiler.Compile([("Lower.cs", "namespace Example;\n\n/// <summary>A type.</summary>\npublic class lower { }\n")]);

		Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("CS8981"));
	}
}
