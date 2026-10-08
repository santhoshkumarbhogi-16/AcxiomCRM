using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers;

[AllowAnonymous]
public class ErrorsController : Controller
{
    [HttpGet("Errors/Error")]
    public IActionResult Error() => View();
}
