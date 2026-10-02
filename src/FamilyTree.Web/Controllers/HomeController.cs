using System.Diagnostics;
using FamilyTree.Data.Repository;
using FamilyTree.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTree.Web.Controllers;

public sealed class HomeController(IPersonRepository repository) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var people = await repository.GetAllAsync(ct);

        var recent = await repository.GetRecentlyChangedAsync(10, ct);

        var model = new DashboardViewModel
        {
            PersonCount = people.Count,
            RecentChanges = [.. recent.Select(r => new RecentChange(r.Person, r.LatestCommit))],
        };

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
