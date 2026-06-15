using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MatricesCheck.Models;

namespace MatricesCheck.Pages;

public class IndexModel : PageModel
{
    public ValidationResult? ValidationResult { get; set; }
    public List<AnomalyGroup> AnomalyGroups { get; set; } = new();
    public bool HasResult { get; set; }

    public void OnGet()
    {
        HasResult = false;
    }

    public IActionResult OnPost(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            ModelState.AddModelError("csvFile", "请选择一个 CSV 文件上传");
            HasResult = false;
            return Page();
        }

        if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("csvFile", "仅支持 .csv 格式文件");
            HasResult = false;
            return Page();
        }

        List<string[]> allRows;
        using (var reader = new StreamReader(csvFile.OpenReadStream(), Encoding.UTF8))
        {
            allRows = ParseCsv(reader);
        }

        if (allRows.Count < 2)
        {
            ModelState.AddModelError("csvFile", "CSV 文件至少需要包含表头行和一行数据");
            HasResult = false;
            return Page();
        }

        ValidationResult = CsvValidator.Validate(allRows);
        AnomalyGroups = CsvValidator.BuildAnomalyGroups(ValidationResult);
        HasResult = true;

        // 生成带注解列的导出 CSV，存入 TempData 供下载
        var exportCsv = GenerateExportCsv(ValidationResult);
        TempData["ExportCsv"] = exportCsv;
        TempData["ExportFileName"] = Path.GetFileNameWithoutExtension(csvFile.FileName) + "_checked.csv";

        return Page();
    }

    public IActionResult OnPostClear()
    {
        TempData.Clear();
        return RedirectToPage();
    }

    public IActionResult OnGetDownload()
    {
        var csv = TempData["ExportCsv"] as string;
        var fileName = TempData["ExportFileName"] as string ?? "result.csv";

        if (string.IsNullOrEmpty(csv))
            return RedirectToPage("/Index");

        // 重存 TempData，因为 GET 请求会消费 TempData
        TempData["ExportCsv"] = csv;
        TempData["ExportFileName"] = fileName;

        var bytes = Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>
    /// 简易 CSV 解析：支持双引号转义字段
    /// </summary>
    private List<string[]> ParseCsv(StreamReader reader)
    {
        var rows = new List<string[]>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue; // 跳过空行

            var fields = ParseCsvLine(line);
            rows.Add(fields.ToArray());
        }
        return rows;
    }

    private List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>
    /// 生成导出 CSV：首列「存在异常值」+ 原始所有列
    /// </summary>
    private string GenerateExportCsv(ValidationResult result)
    {
        var sb = new StringBuilder();

        // 头行
        sb.Append("存在异常值");
        foreach (var h in result.Headers)
        {
            sb.Append(',');
            sb.Append(EscapeCsvField(h));
        }
        sb.AppendLine();

        // 数据行
        foreach (var row in result.Rows)
        {
            sb.Append(row.IsAnomaly ? "是" : "否");
            foreach (var cell in row.Cells)
            {
                sb.Append(',');
                sb.Append(EscapeCsvField(cell));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
