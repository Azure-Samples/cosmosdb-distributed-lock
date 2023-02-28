using CosmosDistributedLock;
using CosmosDistributedLock.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Versioning;

namespace website.Pages;

public class IndexModel : PageModel
{
    public string ErrorMessage = string.Empty;

    public List<DistributedLock> Locks = new List<DistributedLock>();
    
    private LockHelper helper = new LockHelper();

    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ILogger<IndexModel> logger)
    {
        _logger = logger;
    }

    public async Task OnGet()
    {
        await GetLocks();
    }

    public async Task<IActionResult> OnPost(){

        string lockName = Request.Form["LockName"];
        string ownerId = Request.Form["OwnerId"];
        //string ttl = Request.Form["TTL"];

        DistributedLock newLock = new DistributedLock();
        newLock.LockName = lockName;
        newLock.OwnerId = ownerId;
        //newLock.Ttl = int.Parse(ttl);

        //try to get a lock...
        try
        {
            await helper.SaveLock(newLock);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        //update the locks
        await GetLocks();

        return RedirectToAction("Index");


    }

    private async Task GetLocks()
    {
        Locks = (await helper.RetrieveAllLocksAsync()).ToList();
    }
}
