using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KubeCheck.Pages;

public class A2ViewerModel : PageModel
{
    public string HtmlContent { get; set; } = "";
    public string FileName { get; set; } = "";
    public List<string> SheetNames { get; set; } = new();
    public List<string> A2Files { get; set; } = new();

    public IActionResult OnGet(string? file = null)
    {
        var a2Dir = Path.Combine(Directory.GetCurrentDirectory(), "A2");
        if (!Directory.Exists(a2Dir))
            a2Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "A2");

        if (!Directory.Exists(a2Dir))
            return Page();

        var mdFiles = Directory.GetFiles(a2Dir, "*.md");
        A2Files = mdFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
        var targetFile = file != null
            ? mdFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(file, StringComparison.OrdinalIgnoreCase))
                  ?? mdFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(file, StringComparison.OrdinalIgnoreCase))
            : mdFiles.FirstOrDefault();

        if (targetFile == null || !System.IO.File.Exists(targetFile))
            return Page();

        FileName = Path.GetFileNameWithoutExtension(targetFile);
        var lines = System.IO.File.ReadAllLines(targetFile, Encoding.UTF8);
        HtmlContent = ConvertMdToHtml(lines, out var sheets);
        SheetNames = sheets;
        return Page();
    }

    public IActionResult OnGetSection(int index, string? file = null)
    {
        var a2Dir = Path.Combine(Directory.GetCurrentDirectory(), "A2");
        if (!Directory.Exists(a2Dir))
            a2Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "A2");

        var mdFiles = Directory.GetFiles(a2Dir, "*.md");
        var targetFile = file != null
            ? mdFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(file, StringComparison.OrdinalIgnoreCase))
            : mdFiles.FirstOrDefault();
        if (targetFile == null) return Content("");

        var lines = System.IO.File.ReadAllLines(targetFile, Encoding.UTF8);
        var sheets = new List<string>();
        foreach (var line in lines)
            if (line.TrimStart().StartsWith("## "))
                sheets.Add(line.TrimStart()[3..].Trim());

        if (index < 0 || index >= sheets.Count) return Content("");
        var html = ExtractSection(lines, sheets[index]);
        return Content(html, "text/html");
    }

    private string ExtractSection(string[] lines, string sectionName)
    {
        var sb = new StringBuilder();
        bool inTarget = false;
        string? pendingHeader = null;
        bool inTable = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.StartsWith("## ") && line[3..].Trim().Equals(sectionName, StringComparison.OrdinalIgnoreCase))
            {
                inTarget = true;
                sb.Append($"<h4>{MarkdownToHtml(line[3..].Trim())}</h4>");
                continue;
            }
            if (line.StartsWith("## ") && inTarget)
                break; // 下一个 section

            if (!inTarget) continue;

            if (line.StartsWith("### "))
            {
                sb.Append($"<h5>{MarkdownToHtml(line[4..].Trim())}</h5>");
                continue;
            }

            if (line.StartsWith("|") && line.Contains("---"))
            {
                if (!inTable && pendingHeader != null)
                {
                    sb.Append("<div class=\"table-responsive\"><table class=\"table table-bordered table-condensed table-hover\"><thead><tr>");
                    foreach (var c in pendingHeader.Split('|'))
                    {
                        var clean = c.Trim();
                        if (string.IsNullOrWhiteSpace(clean)) continue;
                        sb.Append($"<th>{clean}</th>");
                    }
                    sb.Append("</tr></thead><tbody>");
                    pendingHeader = null;
                    inTable = true;
                }
                continue;
            }

            if (line.StartsWith("|") && inTarget)
            {
                if (inTable)
                {
                    sb.Append("<tr>");
                    var cells = line.Split('|');
                    for (int ci = 1; ci < cells.Length - 1; ci++)
                    {
                        var clean = cells[ci].Trim();
                        if (clean.Contains("√") || clean == "✓")
                            sb.Append("<td class=\"text-center\"><span class=\"label label-success\">√</span></td>");
                        else
                            sb.Append($"<td>{(clean.Length > 80 ? clean[..80] + "…" : clean)}</td>");
                    }
                    sb.Append("</tr>");
                }
                else { pendingHeader = line; }
            }
            else
            {
                if (inTable) { sb.Append("</tbody></table></div>"); inTable = false; }
                pendingHeader = null;
                if (line.StartsWith("> "))
                    sb.Append($"<blockquote>{MarkdownToHtml(line[2..])}</blockquote>");
                else if (line.StartsWith("- **"))
                {
                    var m = Regex.Match(line, @"- \*\*(.+?)\*\*:?\s*(.*)");
                    if (m.Success)
                        sb.Append($"<p><strong>{m.Groups[1].Value}:</strong> {m.Groups[2].Value}</p>");
                }
                else if (!string.IsNullOrWhiteSpace(line))
                    sb.Append($"<p>{MarkdownToHtml(line)}</p>");
            }
        }
        if (inTable) sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    private string ConvertMdToHtml(string[] lines, out List<string> sheets)
    {
        sheets = new List<string>();
        var sb = new StringBuilder();
        bool inTable = false, inCodeBlock = false;
        string? pendingHeader = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            // Code block
            if (line.TrimStart().StartsWith("```"))
            {
                if (inTable) { sb.Append("</tbody></table></div>"); inTable = false; }
                if (inCodeBlock) { sb.Append("</pre>"); inCodeBlock = false; }
                else { inCodeBlock = true; continue; }
            }
            if (inCodeBlock) { sb.Append(line).Append("\n"); continue; }

            // Section header
            if (line.StartsWith("## "))
            {
                if (inTable) { sb.Append("</tbody></table></div>"); inTable = false; }
                var title = line[3..].Trim();
                sheets.Add(title);
                sb.Append($"<h3 id=\"{EscapeId(title)}\">{MarkdownToHtml(title)}</h3>");
                continue;
            }
            if (line.StartsWith("### "))
            {
                if (inTable) { sb.Append("</tbody></table></div>"); inTable = false; }
                var title = line[4..].Trim();
                sb.Append($"<h4>{MarkdownToHtml(title)}</h4>");
                continue;
            }

            // Table separator → 上一行是表头
            if (line.StartsWith("|") && line.Contains("---"))
            {
                if (!inTable)
                {
                    sb.Append("<div class=\"table-responsive\"><table class=\"table table-bordered table-condensed table-hover\">");
                    if (pendingHeader != null)
                    {
                        sb.Append("<thead><tr>");
                        foreach (var c in pendingHeader.Split('|'))
                        {
                            var clean = c.Trim();
                            if (string.IsNullOrWhiteSpace(clean)) continue;
                            sb.Append($"<th>{clean}</th>");
                        }
                        sb.Append("</tr></thead><tbody>");
                        pendingHeader = null;
                    }
                    inTable = true;
                }
                continue;
            }

            // Table row
            if (line.StartsWith("|"))
            {
                if (inTable)
                {
                    // 数据行
                    sb.Append("<tr>");
                    var cells = line.Split('|');
                    for (int ci = 1; ci < cells.Length - 1; ci++)
                    {
                        var clean = cells[ci].Trim();
                        if (clean.Contains("√") || clean == "✓")
                            sb.Append("<td class=\"text-center\"><span class=\"label label-success\">√</span></td>");
                        else
                            sb.Append($"<td>{(clean.Length > 80 ? clean[..80] + "…" : clean)}</td>");
                    }
                    sb.Append("</tr>");
                }
                else
                {
                    // 可能是表头，先缓存
                    pendingHeader = line;
                }
            }
            else
            {
                if (inTable) { sb.Append("</tbody></table></div>"); inTable = false; }
                pendingHeader = null;

                if (line.StartsWith("> "))
                    sb.Append($"<blockquote>{MarkdownToHtml(line[2..])}</blockquote>");
                else if (line.StartsWith("- **"))
                {
                    var m = Regex.Match(line, @"- \*\*(.+?)\*\*:?\s*(.*)");
                    if (m.Success)
                        sb.Append($"<p><strong>{m.Groups[1].Value}:</strong> {m.Groups[2].Value}</p>");
                }
                else if (!string.IsNullOrWhiteSpace(line))
                    sb.Append($"<p>{MarkdownToHtml(line)}</p>");
            }
        }
        if (inTable) { sb.Append("</tbody></table></div>"); }
        if (inCodeBlock) { sb.Append("</pre>"); }

        return sb.ToString();
    }

    private string MarkdownToHtml(string text)
    {
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        text = Regex.Replace(text, @"`([^`]+)`", "<code>$1</code>");
        return text;
    }

    private string EscapeId(string s) => s.Replace(" ", "_").Replace(".", "_");

    private string KeepChinese(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var idx = s.IndexOf('\n');
        if (idx > 0) s = s[..idx];
        for (int i = s.Length - 1; i > 0; i--)
        {
            if (s[i] >= 0x2E80 && i + 1 < s.Length && s[i + 1] == ' ')
            {
                var rest = s[(i + 1)..].Trim();
                if (rest.Length > 0 && rest[0] >= 'A' && rest[0] <= 'z')
                    return s[..(i + 1)].Trim();
            }
        }
        return s.Trim();
    }
}
