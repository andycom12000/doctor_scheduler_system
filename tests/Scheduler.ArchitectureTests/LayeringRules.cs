using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Scheduler.ArchitectureTests;

/// <summary>
/// 驗證 docs/ARCHITECTURE.md §3.2 的硬性規則 #1 與層內附帶規則：
/// - Scheduler.Domain 與 Scheduler.Application 不得引用任何 OR-Tools 型別。
///   求解器相依只存在於 Scheduler.Solver，由介面隔離，保留日後替換引擎的可能。
/// - 只有 Scheduler.Persistence 可以引用 EF Core；Application 不引用 Persistence。
///   Repository 介面在 Application、實作在 Persistence。
/// 規則 #2（Shell 只引用 Api）在 Scheduler.Shell 動工的 PR 補。
///
/// 這些規則若被打破，編譯仍會成功，只有這個測試會擋下來。
///
/// 兩層檢查：
///   1. csproj 層級 —— 誰宣告了 Google.OrTools 的 PackageReference。
///   2. 組件層級   —— 編譯後實際帶進來的相依（含間接引入）。
/// 需要兩層是因為 GetReferencedAssemblies() 只列出程式碼真正用到的組件，
/// 專案掛了套件但還沒寫程式時它是空的。
/// </summary>
public class LayeringRules
{
    private static readonly Assembly Domain = typeof(Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(Application.AssemblyMarker).Assembly;

    private static readonly Assembly Persistence = typeof(Persistence.AssemblyMarker).Assembly;

    private const string OrTools = "Google.OrTools";
    private const string EfCore = "Microsoft.EntityFrameworkCore";

    // ---- csproj 層級 ----

    [Theory]
    [InlineData("src/Scheduler.Domain/Scheduler.Domain.csproj")]
    [InlineData("src/Scheduler.Application/Scheduler.Application.csproj")]
    public void 只有_Solver_能宣告_OrTools_套件(string relativePath)
    {
        var offenders = PackageReferences(relativePath)
            .Where(p => p.StartsWith(OrTools, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{relativePath} 宣告了 OR-Tools 套件：{string.Join(", ", offenders)}。" +
            " 求解器相依必須隔離在 Scheduler.Solver，見 docs/ARCHITECTURE.md §3.1。");
    }

    [Fact]
    public void Solver_確實掛著_OrTools_套件()
    {
        // 反向確認：套件若被移除或改名，上面的檢查會變成空轉，這個測試會先失敗。
        var packages = PackageReferences("src/Scheduler.Solver/Scheduler.Solver.csproj");
        Assert.Contains(packages, p => p.StartsWith(OrTools, StringComparison.Ordinal));
    }

    // ---- 組件層級 ----

    [Fact]
    public void Domain_編譯後不得帶入_OrTools() => AssertNoOrToolsAssembly(Domain);

    [Fact]
    public void Application_編譯後不得帶入_OrTools() => AssertNoOrToolsAssembly(Application);

    [Fact]
    public void Domain_不得引用_Application_或_Solver()
    {
        var names = ReferencedNames(Domain);
        Assert.DoesNotContain("Scheduler.Application", names);
        Assert.DoesNotContain("Scheduler.Solver", names);
    }

    [Fact]
    public void Application_不得引用_Solver()
        => Assert.DoesNotContain("Scheduler.Solver", ReferencedNames(Application));

    // ---- EF Core 只在 Persistence ----

    [Fact]
    public void 只有_Persistence_能宣告_EF_Core_套件()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("Scheduler.Persistence.csproj", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(RepositoryRoot, p))
            .Where(p => PackageReferences(p).Any(pkg => pkg.StartsWith(EfCore, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"這些專案宣告了 EF Core 套件：{string.Join(", ", offenders)}。" +
            " 存取層相依必須隔離在 Scheduler.Persistence，見 docs/ARCHITECTURE.md §3.2。");
    }

    [Fact]
    public void Persistence_確實掛著_EF_Core_套件()
    {
        // 反向確認，同 OR-Tools 的做法
        var packages = PackageReferences("src/Scheduler.Persistence/Scheduler.Persistence.csproj");
        Assert.Contains(packages, p => p.StartsWith(EfCore, StringComparison.Ordinal));
    }

    [Fact]
    public void Application_編譯後不得帶入_EF_Core()
    {
        var offenders = ReferencedNames(Application)
            .Where(n => n.StartsWith(EfCore, StringComparison.Ordinal))
            .ToArray();
        Assert.True(offenders.Length == 0, $"Scheduler.Application 引用了 EF Core：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Application_不得引用_Persistence()
        => Assert.DoesNotContain("Scheduler.Persistence", ReferencedNames(Application));

    [Fact]
    public void Application_專案檔不得掛_Persistence()
    {
        // 組件層檢查在「掛了 ProjectReference 但還沒寫程式」時是空的，所以 csproj 層也要守
        var references = ProjectReferences("src/Scheduler.Application/Scheduler.Application.csproj");
        Assert.DoesNotContain(references, r => r.Contains("Scheduler.Persistence", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_編譯後不得帶入_EF_Core()
    {
        var offenders = ReferencedNames(Domain)
            .Where(n => n.StartsWith(EfCore, StringComparison.Ordinal))
            .ToArray();
        Assert.True(offenders.Length == 0, $"Scheduler.Domain 引用了 EF Core：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Persistence_引用_Application_而不是反過來()
        => Assert.Contains("Scheduler.Application", ReferencedNames(Persistence));

    // ---- helpers ----

    private static void AssertNoOrToolsAssembly(Assembly assembly)
    {
        var offenders = ReferencedNames(assembly)
            .Where(n => n.StartsWith(OrTools, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{assembly.GetName().Name} 引用了 OR-Tools：{string.Join(", ", offenders)}。" +
            " 求解器相依必須隔離在 Scheduler.Solver，見 docs/ARCHITECTURE.md §3.1。");
    }

    private static string[] ReferencedNames(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

    private static string[] ProjectReferences(string relativePath)
    {
        var full = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"找不到專案檔：{full}");

        return XDocument.Load(full)
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
    }

    private static string[] PackageReferences(string relativePath)
    {
        var full = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"找不到專案檔：{full}");

        return XDocument.Load(full)
            .Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HospitalScheduler.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
