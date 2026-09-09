using Bunit;
namespace VirtualCompany.Web.Tests;
public sealed partial class SalesMeetingPreparationPageTests
{
    [Fact]
    public void Browser_preparation_keeps_guest_links_separate_from_Teams_presenter_controls()
    {
        using var context = CreateContext(new PreparationHandler { Conferencing = "browser", Ready = true, Session = CreateSession(1, "Discuss next steps") }, browserDiagnostics: true);
        var cut = Render(context);
        cut.WaitForAssertion(() => Assert.Contains("Copy invitation link", cut.Markup));
        Assert.DoesNotContain("/teams/meetings/", cut.Markup);
        Assert.Contains("The organizer controls admission", cut.Markup);
        var output = Environment.GetEnvironmentVariable("VC_BROWSER_UAT_DIRECTORY");
        if (output != null) File.WriteAllText(Path.Combine(output, "preparation-rendered.html"), cut.Markup);
    }
}
