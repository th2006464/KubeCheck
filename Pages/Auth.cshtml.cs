using System.Net;
using System.Net.Mail;
using KubeCheck.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KubeCheck.Pages;

public class AuthModel : PageModel
{
    private readonly IConfiguration _configuration;

    public string? Message { get; set; }
    public string? MessageType { get; set; } // "success" | "danger" | "info"

    public AuthModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void OnGet()
    {
        // 已登录则跳转到首页
        if (Request.Cookies.ContainsKey("KubeCheckAuth"))
        {
            Response.Redirect("/");
        }
    }

    public IActionResult OnPostLogin(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !int.TryParse(code, out _))
        {
            Message = "请输入有效的 6 位数字授权码";
            MessageType = "danger";
            return Page();
        }

        if (AuthCodeStore.Validate(code))
        {
            // 设置认证 Cookie，有效期 7 天
            Response.Cookies.Append("KubeCheckAuth", "1", new CookieOptions
            {
                Expires = DateTime.Now.AddDays(7),
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });
            return RedirectToPage("/Search");
        }

        Message = "授权码无效或已过期，请重新申请";
        MessageType = "danger";
        return Page();
    }

    public IActionResult OnPostRequest()
    {
        try
        {
            var code = AuthCodeStore.GenerateCode();

            var server = _configuration["EmailSettings:SmtpServer"];
            var port = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
            var from = _configuration["EmailSettings:FromAddress"];
            var to = _configuration["EmailSettings:ToAddress"];
            var cc = _configuration["EmailSettings:CcAddress"];

            _ = Task.Run(() =>
            {
                try
                {
                    ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;
                    using var client = new SmtpClient(server!, port)
                    {
                        EnableSsl = true
                    };
                    using var msg = new MailMessage(from!, to!)
                    {
                        Subject = "[KubeCheck] 审批配置校验系统 - 一次性授权码",
                        Body = $"您的授权码为：{code}\n\n" +
                               $"有效期 30 分钟，请在登录页面输入。\n" +
                               $"如非本人操作，请忽略此邮件。\n\n" +
                               $"此邮件由系统自动发送，请勿回复。",
                        BodyEncoding = System.Text.Encoding.UTF8,
                        IsBodyHtml = false
                    };
                    if (!string.IsNullOrWhiteSpace(cc))
                        msg.CC.Add(cc);
                    client.Send(msg);
                }
                catch
                {
                    // fire-and-forget，忽略发送失败
                }
            });

            Message = "授权码已发送至管理员，请联系KubeOA管理员获取";
            MessageType = "success";
        }
        catch
        {
            Message = "邮件发送失败，请联系 IT 部门";
            MessageType = "danger";
        }

        return Page();
    }
}
