using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KubeCheck.Models;

public class SearchEntry
{
    public string TableName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Code { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Relation { get; set; } = "";
    public string Keywords { get; set; } = ""; // 交叉引用搜索词
}

public static class SearchIndex
{
    private static readonly List<SearchEntry> _entries = new();

    public static int Count => _entries.Count;
    public static bool IsLoaded { get; private set; }

    public static void Load(string searchDir)
    {
        if (!Directory.Exists(searchDir)) return;
        _entries.Clear();

        foreach (var file in Directory.GetFiles(searchDir, "*.csv"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var lines = File.ReadAllLines(file, Encoding.UTF8);
            if (lines.Length < 2) continue;

            var header = ParseCsvLine(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                if (fields.Count < 2) continue;

                var row = header.Zip(fields.PadRight(header.Count), (h, v) => new { h, v })
                    .GroupBy(x => CleanHeader(x.h))
                    .ToDictionary(g => g.Key, g => StripQuotes(g.First().v));

                // UserList 特殊处理
                if (name.Contains("UserList", StringComparison.OrdinalIgnoreCase))
                {
                    // 先收集所有行用于构建汇报关系
                    var userRows = new List<Dictionary<string, string>>();
                    for (int j = i; j < lines.Length; j++)
                    {
                        var f2 = ParseCsvLine(lines[j]);
                        if (f2.Count < 2) continue;
                        var r2 = header.Zip(f2.PadRight(header.Count), (h, v) => new { h, v })
                            .GroupBy(x => CleanHeader(x.h))
                            .ToDictionary(g => g.Key, g => StripQuotes(g.First().v));
                        userRows.Add(r2);
                    }

                    // 构建映射表
                    var idToName = new Dictionary<string, string>();
                    var idToManager = new Dictionary<string, string>();
                    var mgrToSubs = new Dictionary<string, List<string>>(); // 上级→下级列表
                    foreach (var r2 in userRows)
                    {
                        var eid = GetVal(r2, "EmployeeID", "employeeid");
                        var dn = GetVal(r2, "DisplayName", "displayname");
                        var mgr = GetVal(r2, "Manager", "manager");
                        if (!string.IsNullOrWhiteSpace(eid) && !string.IsNullOrWhiteSpace(dn))
                            idToName[eid] = dn;
                        if (!string.IsNullOrWhiteSpace(eid) && !string.IsNullOrWhiteSpace(mgr))
                            idToManager[eid] = mgr;
                        if (!string.IsNullOrWhiteSpace(mgr) && !string.IsNullOrWhiteSpace(dn))
                        {
                            if (!mgrToSubs.ContainsKey(mgr)) mgrToSubs[mgr] = new List<string>();
                            mgrToSubs[mgr].Add(dn);
                        }
                    }

                    foreach (var r2 in userRows)
                    {
                        var displayName = GetVal(r2, "DisplayName", "displayname");
                        var empId = GetVal(r2, "EmployeeID", "employeeid");
                        var email = GetVal(r2, "Email", "email");
                        var dept = GetVal(r2, "Department", "department");
                        var company = GetVal(r2, "Company", "company");
                        var cost = GetVal(r2, "CostCenter", "costcenter");
                        var region = GetVal(r2, "Region", "region");
                        var groupVal = GetVal(r2, "Group", "group");
                        var position = GetVal(r2, "Position", "position");

                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // 向上汇报链（最多5层）
                        var upChain = new List<string>();
                        var current = empId;
                        var visited = new HashSet<string>();
                        while (!string.IsNullOrWhiteSpace(current) && upChain.Count < 5
                               && visited.Add(current) && idToManager.ContainsKey(current))
                        {
                            current = idToManager[current];
                            if (idToName.ContainsKey(current))
                                upChain.Add(idToName[current]);
                        }

                        // 向下汇报链（直接下级，最多10人）
                        var subs = mgrToSubs.ContainsKey(empId) ? mgrToSubs[empId] : new List<string>();
                        var downChain = subs.Count > 0 ? $"🔽{string.Join(", ", subs.Take(10))}" : "";
                        if (subs.Count > 10) downChain += $" ...(共{subs.Count}人)";

                        var reportLine = "";
                        if (upChain.Count > 0) reportLine += $"🔼{string.Join(" → ", upChain)}";
                        if (!string.IsNullOrWhiteSpace(downChain))
                        {
                            if (reportLine.Length > 0) reportLine += "<br>";
                            reportLine += downChain;
                        }

                        _entries.Add(new SearchEntry
                        {
                            TableName = "员工信息",
                            DisplayName = displayName,
                            Code = empId,
                            Detail = $"📧{email} | 🏢{company} | 📂{dept} | 💰{cost} | 📍{region} | 👥{groupVal}",
                            Relation = reportLine
                        });
                    }
                    break; // 跳出，不重复处理 UserList
                }
                else
                {
                    // 项目/部门表
                    var itemName = GetVal(row, "ItemName", "itemname");
                    var itemCode = GetVal(row, "ItemCode", "itemcode");
                    var parent = GetVal(row, "ParentItemCode", "parentitemcode");
                    var isActive = GetVal(row, "IsActive", "isactive");
                    var owners = GetVal(row, "ItemOwners", "itemowners");

                    if (!string.IsNullOrWhiteSpace(itemName))
                    {
                        var tableLabel = GetTableLabel(name);
                        _entries.Add(new SearchEntry
                        {
                            TableName = tableLabel,
                            DisplayName = itemName,
                            Code = itemCode,
                            Detail = parent,
                            Relation = (isActive.Equals("True", StringComparison.OrdinalIgnoreCase) ? "🟢启用" : "⚪停用")
                                      + (string.IsNullOrEmpty(owners) ? "" : $" | 👤{owners}")
                        });
                    }
                }
            }
        }

        // 加载 A2 审批矩阵 Markdown
        var a2Dir = Path.Combine(Path.GetDirectoryName(searchDir) ?? searchDir, "A2");
        if (Directory.Exists(a2Dir))
        {
            foreach (var file in Directory.GetFiles(a2Dir, "*.md"))
                LoadA2Markdown(file);
        }

        EnrichCrossReferences();
        IsLoaded = true;
    }

    /// <summary>
    /// 交叉引用：为每条索引补充关联搜索词
    /// </summary>
    private static void EnrichCrossReferences()
    {
        // 收集所有关键词映射
        var empByDept = new Dictionary<string, List<string>>(); // dept → employee names
        var empByCost = new Dictionary<string, List<string>>(); // cost → employee names
        var empById = new Dictionary<string, SearchEntry>();    // empId → entry
        var deptByCode = new Dictionary<string, string>();      // code → dept name
        var a2Entries = new List<SearchEntry>();

        foreach (var e in _entries)
        {
            if (e.TableName == "员工信息")
            {
                // 从 Detail 中提取部门和成本中心
                var dm = Regex.Match(e.Detail, @"📂(.+?)\s*\|");
                var cm = Regex.Match(e.Detail, @"💰(.+?)\s*\|");
                if (dm.Success) { var d = dm.Groups[1].Value.Trim(); if (!empByDept.ContainsKey(d)) empByDept[d] = new(); empByDept[d].Add(e.DisplayName); }
                if (cm.Success) { var c = cm.Groups[1].Value.Trim(); if (!empByCost.ContainsKey(c)) empByCost[c] = new(); empByCost[c].Add(e.DisplayName); }
                if (!string.IsNullOrWhiteSpace(e.Code)) empById[e.Code] = e;
            }
            else if (e.TableName.Contains("审批矩阵"))
            {
                a2Entries.Add(e);
            }
            else if (e.TableName == "费用承担部门")
            {
                if (!string.IsNullOrWhiteSpace(e.Code)) deptByCode[e.Code] = e.DisplayName;
            }
        }

        // 补充交叉引用关键词
        foreach (var e in _entries)
        {
            var kws = new List<string>();

            if (e.TableName == "员工信息")
            {
                // 部门名 → 关联部门码
                var dm = Regex.Match(e.Detail, @"📂(.+?)\s*\|");
                if (dm.Success) kws.Add(dm.Groups[1].Value.Trim());

                // 所属部门的员工互相可搜
                var cm = Regex.Match(e.Detail, @"💰(.+?)\s*\|");
                if (cm.Success && empByCost.ContainsKey(cm.Groups[1].Value.Trim()))
                {
                    var costCode = cm.Groups[1].Value.Trim();
                    kws.Add(costCode);
                    if (deptByCode.ContainsKey(costCode))
                        kws.Add(deptByCode[costCode]);
                }
            }

            if (e.TableName.Contains("审批矩阵"))
            {
                // 审批矩阵条目 → 反向关联：审批人角色名可搜到对应条目
                if (!string.IsNullOrWhiteSpace(e.Detail))
                {
                    foreach (var role in e.Detail.Split(" → "))
                        kws.Add(role.Trim());
                }
                // 分类名、项目名作为关键词
                kws.Add(e.DisplayName);
            }

            if (kws.Count > 0)
                e.Keywords = string.Join(" ", kws.Distinct());
        }
    }

    private static void LoadA2Markdown(string filePath)
    {
        try
        {
            var division = filePath.Contains("粮油") ? "粮油" : "食品";
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            string sheetName = "", parentCode = "", parentItem = "";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Sheet 名
                if (line.StartsWith("## "))
                {
                    sheetName = KeepChinese(line[3..].Trim());
                    if (sheetName.Contains("Approval") || sheetName.Contains("Instruction"))
                        sheetName = "";
                    parentCode = ""; parentItem = "";
                    continue;
                }

                if (string.IsNullOrWhiteSpace(sheetName)) continue;

                // 找表头
                if (!line.StartsWith("| Code |")) continue;
                var headers = line.Split('|').Select(h => h.Trim()).ToArray();
                i++; // 跳过分隔行

                // 找审批人列起始（Initiator 之后）
                int approverStart = 6; // Code=1, Item=2, Sub=3, Amt=4, Init=5
                var approverNames = new List<string>();
                for (int c = approverStart; c < headers.Length; c++)
                {
                    var h = KeepChinese(headers[c]);
                    if (!string.IsNullOrWhiteSpace(h) && !h.Contains("---"))
                        approverNames.Add(h);
                }

                // 读数据行
                i++;
                while (i < lines.Length && lines[i].Trim().StartsWith("|"))
                {
                    var cells = lines[i].Split('|').Select(c => c.Trim()).ToArray();
                    if (cells.Length < 5) { i++; continue; }

                    var code = KeepChinese(cells[1]);
                    var item = KeepChinese(cells[2]);
                    var sub = KeepChinese(cells[3]);
                    var amt = KeepChinese(cells[4]);
                    var init = cells.Length > 5 ? KeepChinese(cells[5]) : "";

                    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(item) && string.IsNullOrWhiteSpace(sub))
                    { i++; continue; }

                    if (!string.IsNullOrWhiteSpace(code)) parentCode = code;
                    if (!string.IsNullOrWhiteSpace(item) && !item.StartsWith("N") && !item.Contains("Appr"))
                        parentItem = item;

                    var dispCode = string.IsNullOrWhiteSpace(code) ? parentCode : code;
                    var dispItem = string.IsNullOrWhiteSpace(item) ? parentItem : item;

                    // 审批人链
                    var chain = new List<string>();
                    for (int c = approverStart; c < cells.Length; c++)
                    {
                        var cell = cells[c];
                        if (cell.Contains("√") || cell.Contains("✓"))
                        {
                            var idx = c - approverStart;
                            if (idx < approverNames.Count)
                                chain.Add(approverNames[idx]);
                        }
                    }

                    var displayName = string.Join(" | ", new[] { dispItem, sub }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (displayName.Length > 300) displayName = displayName[..300];

                    if (chain.Count > 0 && (!string.IsNullOrWhiteSpace(sub) || !string.IsNullOrWhiteSpace(item)))
                    {
                        _entries.Add(new SearchEntry
                        {
                            TableName = $"{division}审批矩阵 - {sheetName}",
                            DisplayName = displayName,
                            Code = dispCode,
                            Detail = string.Join(" → ", chain),
                            Relation = $"💰{amt} | 👤{init}"
                        });
                    }
                    i++;
                }
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(filePath) ?? ".", "a2_load_error.log"),
                $"{DateTime.Now}: LoadA2 [{Path.GetFileName(filePath)}] - {ex.Message}\n"); } catch { }
        }
    }

    private static void LoadXlsx(string filePath)
    {
        try
        {
            using var wb = new XLWorkbook(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var division = fileName.Contains("粮油") ? "粮油" : "食品";

            foreach (var ws in wb.Worksheets)
            {
                var sheetName = KeepChinese(ws.Name);
                if (string.IsNullOrWhiteSpace(sheetName) || sheetName.Contains("Approval") || sheetName.Contains("Instruction"))
                    continue;

                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                if (lastRow < 6) continue;
                var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 10;

                // 收集审批人表头
                var approverHeaders = new List<string>();
                for (int c = 6; c <= lastCol; c++)
                {
                    var h = ws.Cell(5, c).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(h)) approverHeaders.Add(h);
                }

                string parentCode = "", parentCategory = "";
                for (int r = 6; r <= lastRow; r++)
                {
                    var code = ws.Cell(r, 2).GetString().Trim();      // Col 2 = Code
                    var category = ws.Cell(r, 3).GetString().Trim();   // Col 3 = 分类
                    var desc = ws.Cell(r, 4).GetString().Trim();       // Col 4 = 描述/子项
                    var amount = ws.Cell(r, 5).GetString().Trim();     // Col 5 = 金额
                    var initiator = ws.Cell(r, 6).GetString().Trim();  // Col 6 = 申请人

                    // 跳过全空行
                    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(category)
                        && string.IsNullOrWhiteSpace(desc)) continue;
                    if (code.Contains("序号") || code.Contains("Code")) continue;

                    // 继承父级
                    if (!string.IsNullOrWhiteSpace(code) && code.Length < 30) parentCode = code;
                    if (!string.IsNullOrWhiteSpace(category) && !category.StartsWith("N") && !category.Contains("TPM"))
                        parentCategory = category;

                    // 去掉英文（只保留换行符前的中文部分）
                    code = KeepChinese(code);
                    category = KeepChinese(category);
                    desc = KeepChinese(desc);
                    amount = KeepChinese(amount);
                    initiator = KeepChinese(initiator);
                    for (int i = 0; i < approverHeaders.Count; i++)
                        approverHeaders[i] = KeepChinese(approverHeaders[i]);
                    parentCode = KeepChinese(parentCode);
                    parentCategory = KeepChinese(parentCategory);

                    var dispCode = string.IsNullOrWhiteSpace(code) ? parentCode : code;
                    var dispCategory = string.IsNullOrWhiteSpace(category) ? parentCategory : category;
                    var displayName = string.Join(" | ",
                        new[] { dispCode, dispCategory, desc }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (displayName.Length > 300) displayName = displayName[..300];

                    // 审批人链
                    var chain = new List<string>();
                    for (int c = 6; c <= lastCol; c++)
                    {
                        var val = ws.Cell(r, c).GetString().Trim();
                        if (val.Contains("√") || val.Contains("✓"))
                        {
                            var idx = c - 6;
                            if (idx < approverHeaders.Count)
                                chain.Add(approverHeaders[idx]);
                        }
                    }
                    var detail = chain.Count > 0 ? string.Join(" → ", chain) : "";
                    var relation = $"💰{amount} | 👤{initiator}";

                    if ((!string.IsNullOrWhiteSpace(desc) || !string.IsNullOrWhiteSpace(dispCategory)) && chain.Count > 0)
                    {
                        _entries.Add(new SearchEntry
                        {
                            TableName = $"{division}审批矩阵 - {sheetName}",
                            DisplayName = displayName,
                            Code = dispCode,
                            Detail = detail,
                            Relation = relation
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(filePath) ?? ".", "load_error.log"),
                $"{DateTime.Now}: LoadXlsx [{Path.GetFileName(filePath)}] - {ex.Message}\n"); } catch { }
        }
    }

    public static List<SearchEntry> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return _entries;
        // 拆分关键词（按空格、-、/ 分割），每个部分独立匹配
        var parts = keyword.ToLower().Split(' ', '-', '/', '（', '）', '(', ')', '|')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        return _entries.Where(e =>
        {
            var text = (e.DisplayName + " " + e.Code + " " + e.Detail + " " + e.Relation + " " + e.Keywords).ToLower();
            // 全关键词匹配 或 任一关键词片段匹配
            return text.Contains(keyword.ToLower().Trim())
                   || parts.Any(p => text.Contains(p));
        }).ToList();
    }

    private static string GetTableLabel(string fileName)
    {
        if (fileName.Contains("粮油") && fileName.Contains("QCF")) return "粮油QCF项目";
        if (fileName.Contains("食品") && fileName.Contains("QCF")) return "食品QCF项目";
        if (fileName.Contains("粮油") && fileName.Contains("CAPEX")) return "粮油CAPEX项目";
        if (fileName.Contains("食品") && fileName.Contains("CAPEX")) return "食品CAPEX项目";
        if (fileName.Contains("粮油") && fileName.Contains("用印")) return "粮油用印项目";
        if (fileName.Contains("食品") && fileName.Contains("用印")) return "食品用印项目";
        if (fileName.Contains("AGRI") && fileName.Contains("请款")) return "AGRI请款项目";
        if (fileName.Contains("FOOD") && fileName.Contains("请款")) return "FOOD请款项目";
        if (fileName.Contains("费用承担部门")) return "费用承担部门";
        return fileName;
    }

    /// <summary>保留中文部分，去掉英文翻译</summary>
    private static string KeepChinese(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        // 取第一个换行符前的内容
        var idx = s.IndexOf('\n');
        if (idx > 0) s = s[..idx];
        // 从后往前找：中文+空格+英文模式，在空格处截断
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

    private static string CleanHeader(string h) => h.Trim().Replace("\"", "").ToLower();

    private static string StripQuotes(string s)
    {
        s = s.Trim();
        if (s.StartsWith('"') && s.EndsWith('"')) return s[1..^1];
        return s;
    }

    private static string GetVal(Dictionary<string, string> row, params string[] keys)
    {
        foreach (var k in keys)
            if (row.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return "";
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQ = false;
        var cur = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else inQ = false; } else cur.Append(c); }
            else { if (c == '"') inQ = true; else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); } else cur.Append(c); }
        }
        fields.Add(cur.ToString());
        return fields;
    }
}

internal static class ListExtensions
{
    public static List<string> PadRight(this List<string> list, int count)
    {
        while (list.Count < count) list.Add("");
        return list;
    }
}
