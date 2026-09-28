namespace ApiWeld.Tests;

public class HarnessTests
{
	// Subject: the test host itself. A test project missing its runner adapter
	// builds cleanly and reports zero tests discovered — which exits zero and
	// looks exactly like success. This test only proves the host discovers and
	// runs tests at all; it is not testing anything about ApiWeld.
	[Fact]
	public void Test_host_discovers_and_runs_tests()
	{
		Assert.True(true);
	}
}
