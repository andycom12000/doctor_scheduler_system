using Scheduler.Domain.Defaults;
using Scheduler.Persistence.Seed;

namespace Scheduler.Persistence.Tests;

/// <summary>#82：名冊檔的解析與驗證。內容全是假名；publish.ps1 的驗證規則要與這裡一致。</summary>
public class RosterParseTests
{
    private static readonly string[] Ranks = DefaultRanks.Ranks.Select(r => r.Code).ToArray();

    [Fact]
    public void 合法檔案_略過空白列與Excel的尾端逗號列()
    {
        var result = RosterImporter.Parse("﻿員編,姓名,身分\r\nT001,測試甲,PGY1\r\n,,\r\n\r\nT002, 測試乙 ,NP\r\n", Ranks);
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { new RosterEntry("T001", "測試甲", "PGY1"), new RosterEntry("T002", "測試乙", "NP") }, result.Members);
    }

    [Theory]
    [InlineData("編號,姓名,身分\nT001,測試甲,R1\n", "第 1 列")]
    [InlineData("員編,姓名,身分\nT001,測試甲,R1\n,測試乙,R1\n", "第 3 列：員編是空的")]
    [InlineData("員編,姓名,身分\nT001,測試甲,R1\nT001,測試乙,R1\n", "第 3 列：員編與前面的列重複")]
    [InlineData("員編,姓名,身分\nT001,,R1\n", "第 2 列：姓名是空的")]
    [InlineData("員編,姓名,身分\nT001,測試甲,R9\n", "第 2 列：不認得的身分代碼")]
    [InlineData("員編,姓名,身分\nT001,測試甲\n", "第 2 列：欄位數")]
    [InlineData("員編,姓名,身分\n", "沒有任何人員列")]
    public void 驗證錯誤_指出列號(string content, string expected)
    {
        var result = RosterImporter.Parse(content, Ranks);
        Assert.Contains(result.Errors, e => e.Contains(expected));
    }

    [Fact]
    public void publish腳本認得的身分代碼與出廠身分一致()
    {
        // build/publish.ps1 自己驗一次（發佈時沒有 .NET 型別可用），代碼清單寫死在那裡，這裡守住不漂移
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "build", "publish.ps1")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        var script = File.ReadAllText(Path.Combine(dir!, "build", "publish.ps1"));
        var line = script.Split('\n').Single(l => l.Contains("$knownRankCodes ="));
        var codes = line[(line.IndexOf('=') + 1)..].Replace("@(", "").Replace(")", "").Replace("'", "").Split(',').Select(c => c.Trim()).Where(c => c.Length > 0);
        Assert.Equal(Ranks.OrderBy(c => c), codes.OrderBy(c => c));
    }
}
