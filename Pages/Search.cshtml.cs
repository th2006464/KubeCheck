using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using KubeCheck.Models;

namespace KubeCheck.Pages;

public class SearchModel : PageModel
{
    public List<SearchEntry> Results { get; set; } = new();
    public string Keyword { get; set; } = "";
    public bool Searched { get; set; }

    private IActionResult CheckAuth()
    {
        if (!Request.Cookies.ContainsKey("KubeCheckAuth"))
            return RedirectToPage("/Auth");
        return null!;
    }

    public IActionResult OnGet()
    {
        var auth = CheckAuth();
        if (auth != null!) return auth;
        Searched = false;
        return Page();
    }

    public IActionResult OnPostReload()
    {
        var auth = CheckAuth();
        if (auth != null!) return auth;

        var searchDir = Path.Combine(Directory.GetCurrentDirectory(), "search");
        if (!Directory.Exists(searchDir))
            searchDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "search");
        SearchIndex.Load(searchDir);
        return RedirectToPage();
    }

    public IActionResult OnPost(string keyword)
    {
        var auth = CheckAuth();
        if (auth != null!) return auth;
        Keyword = keyword?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            Results = SearchIndex.Search(Keyword);
        }
        Searched = true;
        return Page();
    }
}
