using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FamilyTree.Web.Infrastructure;

/// <summary>When a request carries the HX-Request header, renders views without the shared layout, so
/// the response is just the fragment HTMX will swap into the page. This means most actions can simply
/// `return View(model)` and get correct behavior for both full-page and HTMX-fragment requests.</summary>
public sealed class HtmxLayoutResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        var isHtmxRequest = context.HttpContext.Request.Headers.ContainsKey("HX-Request");

        if (isHtmxRequest && context.Result is ViewResult viewResult)
        {
            viewResult.ViewData["Layout"] = (string?)null;
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
