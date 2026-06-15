# MatricesCheck — 审批配置数据校验系统

基于 .NET 8 + ASP.NET Core Razor Pages 的 CSV 审批配置校验工具，自动识别条件列与审批人列，按条件组执行规则校验，并以 Bootstrap 3 页面展示异常结果。

## 技术栈

| 层级 | 技术 |
|------|------|
| 后端框架 | .NET 8 + ASP.NET Core Razor Pages |
| 前端 UI | Bootstrap 3（百度 CDN 远端引用） |
| 部署 | Windows Server + IIS（ASP.NET Core Module） |
| 开发调试 | Kestrel（`dotnet run`） |

## 功能

- **CSV 上传校验**：上传 UTF-8 编码的 CSV 文件，自动解析并校验
- **自动列识别**：自动检测 `role` / `role1` ~ `role20` 审批人列，其左侧所有列为条件列
- **智能排除**：`id` 列及所有值唯一的列自动排除，不参与条件分组
- **三条校验规则**（同一条件组内）：
  1. 审批人数不一致
  2. 审批人员/排列顺序不一致
  3. 存在重复审批配置
- **合并展示**：异常结果按条件组合并显示，支持展开查看明细
- **CSV 导出**：下载带「存在异常值」标识列的校验结果 CSV

## 项目结构

```
MatricesCheck/
├── Pages/
│   ├── Index.cshtml              # 主页面
│   ├── Index.cshtml.cs           # PageModel（上传、解析、校验）
│   ├── Error.cshtml / .cs        # 错误页
│   ├── Shared/_Layout.cshtml     # Bootstrap 3 壳页面
│   ├── _ViewImports.cshtml       # 全局引用
│   └── _ViewStart.cshtml
├── Models/
│   └── CsvValidator.cs           # 核心校验引擎（纯逻辑，无依赖）
├── wwwroot/
│   └── logo.png                  # 页头 Logo（需自行放置）
├── Program.cs                    # 应用入口 + Session 配置
├── MatricesCheck.csproj          # 项目文件
├── appsettings.json              # 生产配置
└── .gitignore
```

## 环境要求

### 开发环境

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows / macOS / Linux

### 部署环境

- Windows Server 2016+（推荐 2019/2022）
- IIS 10+
- [.NET 8 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0)（安装后需重启 IIS）

---

## 本地开发调试

```bash
# 1. 克隆项目
git clone <repo-url>
cd MatricesCheck

# 2. 还原依赖 + 运行
dotnet run --urls "http://localhost:5000"

# 3. 浏览器打开
# http://localhost:5000
```

---

## 部署到 IIS

### 1. 服务器准备

在 Windows Server 上安装 [.NET 8 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0)：

```
https://dotnet.microsoft.com/download/dotnet/8.0
→ 下载 "ASP.NET Core Runtime 8.x.x — Windows Hosting Bundle"
→ 安装后以管理员身份运行：iisreset
```

### 2. 发布项目

在开发机上执行：

```bash
dotnet publish -c Release -o release
```

将 `release\` 目录的全部内容复制到服务器的目标文件夹，例如：

```
D:\WebApps\MatricesCheck\
```

### 3. IIS 站点配置

1. 打开 **IIS 管理器**
2. 右键「网站」→「添加网站」
3. 填写配置：

   | 设置项 | 值 |
   |--------|-----|
   | 网站名称 | `MatricesCheck` |
   | 物理路径 | `D:\WebApps\MatricesCheck` |
   | 绑定类型 | `https` |
   | 主机名 | `www.garchina.com`（按实际域名填写） |
   | SSL 证书 | 选择对应的 HTTPS 证书 |

4. **应用程序池**设置：
   - .NET CLR 版本：`无托管代码`
   - 托管管道模式：`集成`
   - 标识：`ApplicationPoolIdentity`（或按需配置）

### 4. 子路径部署（可选）

如需部署到子路径（如 `/MatricesCheck`），在 IIS 中：

1. 右键目标网站 →「添加应用程序」
2. 别名：`MatricesCheck`
3. 物理路径：`D:\WebApps\MatricesCheck`
4. 应用程序池选择对应应用池

项目中的 `@Url.Content("~/...")` 和 `@Url.Page(...)` 会自动适配子路径。

### 5. 文件权限

确保 IIS 应用池标识对以下目录有**读取+执行**权限：

```
D:\WebApps\MatricesCheck\
```

如需写入日志，额外对 `logs\` 目录赋予**写入**权限。

### 6. 验证部署

浏览器访问 `https://<域名>/MatricesCheck`（或配置的路径），看到上传页面即部署成功。

---

## CSV 文件格式要求

### 编码

UTF-8（无 BOM 或带 BOM 均可）

### 列结构

```
条件列1, 条件列2, ..., 条件列N, role, role1, role2, ..., role20
```

- **条件列**：第一个 `role` 系列列左侧的所有列（列数不固定，程序自动识别）
- **审批人列**：`role`、`role1`、`role2` ... `role20`，最大支持 20 列，空值表示无对应审批人
- **`id` 列**：自动排除，不参与条件分组
- **`*` 字符**：在条件列中按普通文本匹配，不做通配符解析

### 示例

```csv
InvolvedCompany,SubmittingItem,InvolvedDepartments,InvolvedAmount,Role1,Role2,Role3,Role4,Role5
金光食品,采购申请A,采购部,*,采购总监,财务经理,CFO,,
金光食品,采购申请A,采购部,*,采购总监,财务经理,CFO,CEO,
华丰食品,采购申请B,工厂,*,厂长,采购经理,,,
华丰食品,采购申请B,工厂,*,厂长,采购经理,,,
```

---

## 校验规则详解

校验范围：**同一条件组内**（条件列值完全相同的行划分为同一组，单行组不校验）

| 规则 | 判定逻辑 | 异常文案 |
|------|----------|----------|
| 审批人数不一致 | 组内各行的有效审批人数（非空单元格）不统一 | 同一条件组内审批人数不一致 |
| 人员/排列不一致 | 组内审批人姓名、排列顺序、空值分布存在差异 | 同一条件组内审批人员/排列顺序不一致 |
| 配置完全重复 | 组内多行的审批人列内容、顺序、空值完全一致 | 同一条件组内存在重复审批配置 |

任意命中一条，该行即标记为异常。

---

## 配置文件

### appsettings.json

生产环境配置，可按需调整：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### Session 配置

Session 存储在服务端内存中（`Program.cs`）：

```csharp
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);  // 30分钟过期
});
```

单机 IIS 部署无需额外配置。如需多机负载均衡，将 Session 改为 Redis 或 SQL Server 存储。

---

## 常见问题

### Q: Logo 不显示？

将 `logo.png`（87KB，50px 高度）放到 `wwwroot\` 目录下。路径使用 `@Url.Content("~/logo.png")` 会自动适配子路径部署。

### Q: 上传后 500 错误？

1. 检查 CSV 文件是否为 UTF-8 编码
2. 检查 Application Pool 是否有「无托管代码」设置
3. 检查 .NET 8 Hosting Bundle 是否已安装

### Q: 多用户同时使用会冲突吗？

不会。每个用户的 Session 独立，校验引擎是无状态的静态方法。

### Q: 端口冲突（开发环境）？

使用 `release\run_dev.bat` 启动，会自动检测并清理占用的端口。
