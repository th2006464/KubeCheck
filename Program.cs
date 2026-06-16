using KubeCheck.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Search", "/");
}).AddSessionStateTempDataProvider();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10 MB CSV upload limit
});

var app = builder.Build();

// 加载搜索索引（所有 search 目录下的 CSV）
var searchDir = Path.Combine(app.Environment.ContentRootPath, "search");
if (!Directory.Exists(searchDir))
    searchDir = Path.Combine(Directory.GetCurrentDirectory(), "search");
SearchIndex.Load(searchDir);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseSession();
app.UseRouting();
app.MapRazorPages();
app.Run();
