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

    // 步骤1：列配置面板
    public bool ShowColumnConfig { get; set; }
    public string[] Headers { get; set; } = Array.Empty<string>();
    public List<int> AutoDetectedRoleCols { get; set; } = new();
    public string UploadedFileName { get; set; } = "";

    public void OnGet()
    {
        HasResult = false;
        ShowColumnConfig = false;
    }

    /// <summary>
    /// 步骤1：上传 CSV → 展示列选择面板
    /// </summary>
    public IActionResult OnPost(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            ModelState.AddModelError("csvFile", "请选择一个 CSV 文件上传");
            return Page();
        }

        if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("csvFile", "仅支持 .csv 格式文件");
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
            return Page();
        }

        Headers = allRows[0];
        UploadedFileName = csvFile.FileName;
        var (_, autoRoleCols) = CsvValidator.DetectColumns(Headers);
        AutoDetectedRoleCols = autoRoleCols;
        ShowColumnConfig = true;

        // 将原始 CSV 以 tab 分隔格式存入 TempData，供步骤2使用
        var sb = new StringBuilder();
        foreach (var row in allRows)
        {
            sb.AppendLine(string.Join("\t", row.Select(EscapeForStorage)));
        }
        TempData["RawCsv"] = sb.ToString();
        TempData["FileName"] = csvFile.FileName;

        return Page();
    }

    /// <summary>
    /// 步骤2：用户选定第一个审批人列 → 推导全部审批人列 → 执行校验
    /// </summary>
    public IActionResult OnPostValidate(int firstRoleCol)
    {
        var raw = TempData["RawCsv"] as string;
        var fileName = TempData["FileName"] as string ?? "result.csv";

        if (string.IsNullOrEmpty(raw))
        {
            ModelState.AddModelError("", "上传数据已过期，请重新上传");
            return Page();
        }

        // 还原 TempData 供下载使用
        TempData["RawCsv"] = raw;
        TempData["FileName"] = fileName;

        var allRows = RestoreCsv(raw);
        if (allRows.Count < 2)
            return Page();

        // 从起始列推导全部审批人列（起始列及其右侧所有列）
        var roleColList = new List<int>();
        if (firstRoleCol >= 0)
        {
            for (int i = firstRoleCol; i < allRows[0].Length; i++)
                roleColList.Add(i);
        }

        ValidationResult = CsvValidator.Validate(allRows, roleColList);
        AnomalyGroups = CsvValidator.BuildAnomalyGroups(ValidationResult);
        HasResult = true;
        ShowColumnConfig = false;

        // 生成导出 CSV
        var exportCsv = GenerateExportCsv(ValidationResult);
        TempData["ExportCsv"] = exportCsv;
        TempData["ExportFileName"] = Path.GetFileNameWithoutExtension(fileName) + "_checked.csv";

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

        TempData["ExportCsv"] = csv;
        TempData["ExportFileName"] = fileName;

        var bytes = Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    // ===== 工具方法 =====

    private string EscapeForStorage(string field)
    {
        return field.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private string UnescapeFromStorage(string field)
    {
        return field.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
    }

    private List<string[]> RestoreCsv(string raw)
    {
        var rows = new List<string[]>();
        var lines = raw.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var fields = line.TrimEnd('\r').Split('\t');
            rows.Add(fields.Select(UnescapeFromStorage).ToArray());
        }
        return rows;
    }

    private List<string[]> ParseCsv(StreamReader reader)
    {
        var rows = new List<string[]>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

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

    private string GenerateExportCsv(ValidationResult result)
    {
        var sb = new StringBuilder();

        sb.Append("存在异常值");
        foreach (var h in result.Headers)
        {
            sb.Append(',');
            sb.Append(EscapeCsvField(h));
        }
        sb.AppendLine();

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
