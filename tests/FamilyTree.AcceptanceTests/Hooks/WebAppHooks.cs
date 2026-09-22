using FamilyTree.AcceptanceTests.Support;
using Reqnroll;

namespace FamilyTree.AcceptanceTests.Hooks;

[Binding]
public sealed class WebAppHooks(AppFixture fixture)
{
    [AfterScenario]
    public void DisposeApp() => fixture.Dispose();
}
