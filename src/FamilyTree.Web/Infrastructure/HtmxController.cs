using Microsoft.AspNetCore.Mvc;

namespace FamilyTree.Web.Infrastructure;

public abstract class HtmxController : Controller
{
    protected bool IsHtmxRequest => Request.Headers.ContainsKey("HX-Request");

    /// <summary>After a mutating POST, tells HTMX to navigate the browser to the given URL when the
    /// request came from HTMX (a plain redirect response would otherwise just be swapped into the
    /// current element), or issues a normal redirect for a non-HTMX request.</summary>
    protected IActionResult RedirectAfterSave(string url) => IsHtmxRequest
        ? RedirectViaHtmx(url)
        : Redirect(url);

    private IActionResult RedirectViaHtmx(string url)
    {
        Response.Headers["HX-Redirect"] = url;
        return Ok();
    }
}
