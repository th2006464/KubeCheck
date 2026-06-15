using System.Text.RegularExpressions;

namespace MatricesCheck.Models;

/// <summary>
/// 单行校验结果
/// </summary>
public class RowResult
{
    public int RowIndex { get; set; }
    public bool IsAnomaly { get; set; }
    public List<string> Reasons { get; set; } = new();
    public string[] Cells { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 整体校验结果
/// </summary>
public class ValidationResult
{
    public List<RowResult> Rows { get; set; } = new();
    public string[] Headers { get; set; } = Array.Empty<string>();
    public List<int> ConditionColIndices { get; set; } = new();
    public List<int> RoleColIndices { get; set; } = new();
}

/// <summary>
/// 异常条件组（用于前端合并展示）
/// </summary>
public class AnomalyGroup
{
    public string ConditionDisplay { get; set; } = "";
    public List<string> AnomalyReasons { get; set; } = new();
    public int AnomalyRowCount { get; set; }
    public List<string> RowNumbers { get; set; } = new();
}

public static class CsvValidator
{
    /// <summary>
    /// 自动识别列：找到第一个匹配 ^role\d*$ 的列，其左侧为条件列，匹配列为审批人列
    /// </summary>
    public static (List<int> conditionCols, List<int> roleCols) DetectColumns(string[] headers)
    {
        var roleCols = new List<int>();
        int firstRole = -1;

        for (int i = 0; i < headers.Length; i++)
        {
            if (Regex.IsMatch(headers[i].Trim(), @"^role\d*$", RegexOptions.IgnoreCase))
            {
                roleCols.Add(i);
                if (firstRole < 0) firstRole = i;
            }
        }

        var conditionCols = new List<int>();
        if (firstRole > 0)
        {
            for (int i = 0; i < firstRole; i++)
                conditionCols.Add(i);
        }

        return (conditionCols, roleCols);
    }

    /// <summary>
    /// 主校验入口：按条件组分组后，对每组执行 3 条规则
    /// </summary>
    public static ValidationResult Validate(List<string[]> allRows)
    {
        if (allRows.Count < 2)
            return new ValidationResult();

        var headers = allRows[0];
        var dataRows = allRows.Skip(1).ToList();
        var (rawConditionCols, roleCols) = DetectColumns(headers);

        // 排除 id 列和所有值唯一的列，不作为条件列参与分组
        var conditionCols = ExcludeIdAndUniqueColumns(rawConditionCols, headers, dataRows);

        var result = new ValidationResult
        {
            Headers = headers,
            ConditionColIndices = conditionCols,
            RoleColIndices = roleCols
        };
        var rowResults = new RowResult[dataRows.Count];

        for (int i = 0; i < dataRows.Count; i++)
        {
            rowResults[i] = new RowResult
            {
                RowIndex = i,
                Cells = dataRows[i],
                IsAnomaly = false
            };
        }

        if (roleCols.Count == 0)
        {
            // 没有审批人列，全部正常
            result.Rows = rowResults.ToList();
            return result;
        }

        // 按条件列分组
        var groups = new Dictionary<string, List<int>>();
        for (int i = 0; i < dataRows.Count; i++)
        {
            var key = BuildConditionKey(dataRows[i], conditionCols);
            if (!groups.ContainsKey(key))
                groups[key] = new List<int>();
            groups[key].Add(i);
        }

        // 组内校验
        foreach (var kv in groups)
        {
            var indices = kv.Value;
            if (indices.Count < 2)
                continue; // 单行组无需校验

            // 规则1：审批人数不一致
            CheckRule1(rowResults, indices, roleCols);

            // 规则2：审批人员内容不一致
            CheckRule2(rowResults, indices, roleCols);

            // 规则3：配置完全重复
            CheckRule3(rowResults, indices, roleCols);
        }

        result.Rows = rowResults.ToList();
        return result;
    }

    /// <summary>
    /// 排除 id 系列列和所有值唯一的列，这些列不参与条件分组
    /// </summary>
    private static List<int> ExcludeIdAndUniqueColumns(List<int> conditionCols, string[] headers, List<string[]> dataRows)
    {
        return conditionCols.Where(col =>
        {
            // 列名含 "id"（不区分大小写）始终排除
            if (col < headers.Length)
            {
                var name = headers[col].Trim();
                if (name.Equals("id", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 所有值唯一的列也排除
            var values = new HashSet<string>();
            foreach (var row in dataRows)
            {
                var val = col < row.Length ? row[col] : "";
                if (!values.Add(val))
                    return true; // 出现重复值，该列是有效条件列
            }
            return false; // 所有值唯一，排除
        }).ToList();
    }

    /// <summary>
    /// 将异常行按条件组合并，用于前端分组展示
    /// </summary>
    public static List<AnomalyGroup> BuildAnomalyGroups(ValidationResult result)
    {
        var anomalyRows = result.Rows.Where(r => r.IsAnomaly).ToList();
        if (anomalyRows.Count == 0) return new List<AnomalyGroup>();

        var groups = new Dictionary<string, AnomalyGroup>();

        foreach (var row in anomalyRows)
        {
            var condValues = result.ConditionColIndices
                .Select(c => c < row.Cells.Length ? row.Cells[c] : "")
                .ToArray();
            var key = string.Join("|||", condValues);

            if (!groups.ContainsKey(key))
            {
                groups[key] = new AnomalyGroup
                {
                    ConditionDisplay = string.Join(" | ", condValues),
                    AnomalyReasons = new List<string>(),
                    RowNumbers = new List<string>()
                };
            }

            var g = groups[key];
            g.AnomalyRowCount++;
            g.RowNumbers.Add((row.RowIndex + 2).ToString()); // CSV 行号 = RowIndex + 2（0-index + 表头）

            foreach (var reason in row.Reasons)
            {
                if (!g.AnomalyReasons.Contains(reason))
                    g.AnomalyReasons.Add(reason);
            }
        }

        return groups.Values.ToList();
    }

    private static string BuildConditionKey(string[] row, List<int> conditionCols)
    {
        if (conditionCols.Count == 0) return "_single_group_";
        return string.Join("|||", conditionCols.Select(c => row.Length > c ? row[c] : ""));
    }

    /// <summary>
    /// 规则1：同一条件组内，各行有效审批人数（非空单元格数）不统一 → 异常
    /// </summary>
    private static void CheckRule1(RowResult[] rows, List<int> indices, List<int> roleCols)
    {
        var counts = indices.Select(i => CountNonEmpty(rows[i].Cells, roleCols)).ToList();
        var expected = counts.First();
        bool allSame = counts.All(c => c == expected);

        if (!allSame)
        {
            foreach (var i in indices)
            {
                rows[i].IsAnomaly = true;
                rows[i].Reasons.Add("同一条件组内审批人数不一致");
            }
        }
    }

    /// <summary>
    /// 规则2：同一条件组内，审批人列的人员姓名、排列顺序、空值分布存在差异 → 异常
    /// </summary>
    private static void CheckRule2(RowResult[] rows, List<int> indices, List<int> roleCols)
    {
        var roleValues = indices.Select(i => GetRoleSignature(rows[i].Cells, roleCols)).ToList();
        var first = roleValues.First();
        bool allSame = roleValues.All(v => v == first);

        if (!allSame)
        {
            foreach (var i in indices)
            {
                if (!rows[i].IsAnomaly)
                    rows[i].IsAnomaly = true;
                rows[i].Reasons.Add("同一条件组内审批人员/排列顺序不一致");
            }
        }
    }

    /// <summary>
    /// 规则3：同一条件组内，多行审批人列内容、顺序、空值完全一致（冗余重复配置）→ 异常
    /// </summary>
    private static void CheckRule3(RowResult[] rows, List<int> indices, List<int> roleCols)
    {
        // 统计每种 role 签名的出现次数
        var sigCount = new Dictionary<string, List<int>>();
        foreach (var i in indices)
        {
            var sig = GetRoleSignature(rows[i].Cells, roleCols);
            if (!sigCount.ContainsKey(sig))
                sigCount[sig] = new List<int>();
            sigCount[sig].Add(i);
        }

        // 出现次数 > 1 的签名，对应行标记为重复
        foreach (var kv in sigCount)
        {
            if (kv.Value.Count > 1)
            {
                foreach (var i in kv.Value)
                {
                    if (!rows[i].IsAnomaly)
                        rows[i].IsAnomaly = true;
                    rows[i].Reasons.Add("同一条件组内存在重复审批配置");
                }
            }
        }
    }

    private static int CountNonEmpty(string[] cells, List<int> cols)
    {
        return cols.Count(c => c < cells.Length && !string.IsNullOrWhiteSpace(cells[c]));
    }

    private static string GetRoleSignature(string[] cells, List<int> cols)
    {
        var parts = cols.Select(c => c < cells.Length ? cells[c].Trim() : "");
        return string.Join("\t", parts);
    }
}
