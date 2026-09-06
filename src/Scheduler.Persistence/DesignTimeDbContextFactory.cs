using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scheduler.Persistence;

/// <summary>
/// 只給 <c>dotnet ef migrations add</c> 用：本專案是 class library，沒有 startup project，
/// EF 工具靠這個工廠建 DbContext 來比對模型與 snapshot。指到的檔案不會真的被建立。
/// 用法（在 repo 根目錄）：<c>dotnet ef migrations add &lt;Name&gt; --project src/Scheduler.Persistence</c>
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    public SchedulerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlite("Data Source=design-time-only.db")
            .Options;
        return new SchedulerDbContext(options);
    }
}
