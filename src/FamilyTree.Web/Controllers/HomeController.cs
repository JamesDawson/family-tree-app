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

        var recentChanges = new List<RecentChange>();
        foreach (var person in people)
        {
            var history = await repository.GetHistoryAsync(person.Id, ct);
            if (history.Count > 0)
            {
                recentChanges.Add(new RecentChange(person, history[0]));
            }
        }

        var model = new DashboardViewModel
        {
            PersonCount = people.Count,
            RecentChanges = [.. recentChanges.OrderByDescending(c => c.LatestCommit.When).Take(10)],
        };

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
