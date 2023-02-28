using CosmosDistributedLock;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Versioning;

namespace website.Pages;

public class LockModel : PageModel
{
    public string ErrorMessage = string.Empty;

    private LockHelper helper = new LockHelper();

    private readonly ILogger<IndexModel> _logger;

    public LockModel(ILogger<IndexModel> logger)
    {
        _logger = logger;
    }

    public async Task<IActionResult> OnGet(string lockName, string clientId)
    {
        try
        {
            var gLock = await helper.RetrieveLockAsync(lockName);
            await helper.ReleaseLock(gLock);
        }
        catch
        {

        }

        return Redirect("Index");
    }

    
}
