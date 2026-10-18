using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace SpecFleet.Test.Browser;

public sealed class BrowserSmokeTests : PageTest
{
    [Fact]
    public async Task ChromiumCanRenderAndInspectPageContent()
    {
        await Page.SetContentAsync("<main><h1>Playwright is ready</h1></main>");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Playwright is ready" }))
            .ToBeVisibleAsync();
    }
}
