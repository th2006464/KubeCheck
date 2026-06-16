using System.Collections.Concurrent;

namespace KubeCheck.Models;

/// <summary>
/// 一次性授权码存储（服务端内存），生产环境重启后失效
/// </summary>
public static class AuthCodeStore
{
    private static readonly ConcurrentDictionary<string, DateTime> _codes = new();

    /// <summary>生成6位随机数字授权码</summary>
    public static string GenerateCode()
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        _codes[code] = DateTime.Now;
        return code;
    }

    /// <summary>内置万能授权码</summary>
    public const string MasterCode = "112233";

    /// <summary>验证码是否正确（一次性，验证后即删除。万能码不删除）</summary>
    public static bool Validate(string code)
    {
        if (code == MasterCode)
            return true;

        if (_codes.TryRemove(code, out var created))
        {
            return (DateTime.Now - created).TotalMinutes < 30;
        }
        return false;
    }
}
