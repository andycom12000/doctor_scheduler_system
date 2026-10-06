using System.Text.Json;

namespace Scheduler.Shell.Tests;

public class SaveFileProtocolTests
{
    private static string Message(string id, string fileName, byte[] bytes) =>
        JsonSerializer.Serialize(new { type = "save-file", id, fileName, base64 = Convert.ToBase64String(bytes) });

    [Fact]
    public void 解析存檔訊息_還原位元組與檔名()
    {
        var bytes = new byte[] { 0x50, 0x4B, 3, 4, 0, 255 };

        var outcome = SaveFileProtocol.Parse(Message("a1", "duty-2026-11.xlsx", bytes));

        Assert.True(outcome.IsForUs);
        Assert.NotNull(outcome.Request);
        Assert.Equal("a1", outcome.Request!.Id);
        Assert.Equal("duty-2026-11.xlsx", outcome.Request.FileName);
        Assert.Equal(bytes, outcome.Request.Bytes);
    }

    [Theory]
    [InlineData("""{"jobId":"j1","status":"running"}""")]
    [InlineData("""{"type":"other","id":"x"}""")]
    [InlineData("\"just a string\"")]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    public void 不是存檔訊息_不歸我們處理(string json)
        => Assert.False(SaveFileProtocol.Parse(json).IsForUs);

    [Fact]
    public void 存檔訊息缺欄位或_base64_壞掉_是壞訊息但帶回_id_讓前端收到失敗()
    {
        var noBody = SaveFileProtocol.Parse("""{"type":"save-file","id":"a2","fileName":"x.xlsx"}""");
        Assert.True(noBody.IsForUs);
        Assert.Null(noBody.Request);
        Assert.Equal("a2", noBody.Id);
        Assert.NotNull(noBody.Error);

        var badBase64 = SaveFileProtocol.Parse("""{"type":"save-file","id":"a3","fileName":"x.xlsx","base64":"***"}""");
        Assert.True(badBase64.IsForUs);
        Assert.Null(badBase64.Request);
        Assert.Equal("a3", badBase64.Id);

        var noId = SaveFileProtocol.Parse("""{"type":"save-file","base64":"AA=="}""");
        Assert.True(noId.IsForUs);
        Assert.Null(noId.Request);
        Assert.Null(noId.Id);
    }

    [Fact]
    public void 超過上限的檔案被拒()
    {
        var outcome = SaveFileProtocol.Parse(Message("big", "x.xlsx", new byte[SaveFileProtocol.MaxBytes + 1]));

        Assert.True(outcome.IsForUs);
        Assert.Null(outcome.Request);
        Assert.Equal("big", outcome.Id);
    }

    [Theory]
    [InlineData("duty-2026-11.xlsx", "duty-2026-11.xlsx")]
    [InlineData("班表-2026-11.XLSX", "班表-2026-11.XLSX")]
    [InlineData("duty", "duty.xlsx")]
    [InlineData("report.csv", "report.csv.xlsx")]
    [InlineData(null, "export.xlsx")]
    [InlineData("   ", "export.xlsx")]
    [InlineData("..", "export.xlsx")]
    [InlineData("C:\\Windows\\evil.xlsx", "evil.xlsx")]
    [InlineData("../../evil.xlsx", "evil.xlsx")]
    [InlineData("a:b*c?.xlsx", "a_b_c_.xlsx")]
    [InlineData("a<b>c\"d|e\u0001f.xlsx", "a_b_c_d_e_f.xlsx")]
    public void 檔名只留檔名部分_換掉非法字元_副檔名只認_xlsx(string? requested, string expected)
        => Assert.Equal(expected, SaveFileProtocol.SafeFileName(requested));

    [Theory]
    [InlineData("https://app.local/schedules/2026-11", true)]
    [InlineData("https://APP.LOCAL/", true)]
    [InlineData("https://evil.example/", false)]
    [InlineData("http://app.local/", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 只信任_app_local_的頁面(string? source, bool expected)
        => Assert.Equal(expected, SaveFileProtocol.IsTrustedSource(source));

    [Fact]
    public void 回覆_JSON_帶型別_id_狀態_失敗才有訊息()
    {
        using var saved = JsonDocument.Parse(SaveFileProtocol.ResultJson("a1", SaveFileProtocol.Saved));
        Assert.Equal("save-file-result", saved.RootElement.GetProperty("type").GetString());
        Assert.Equal("a1", saved.RootElement.GetProperty("id").GetString());
        Assert.Equal("saved", saved.RootElement.GetProperty("status").GetString());
        Assert.False(saved.RootElement.TryGetProperty("message", out _));

        using var failed = JsonDocument.Parse(SaveFileProtocol.ResultJson("a2", SaveFileProtocol.Failed, "磁碟已滿"));
        Assert.Equal("error", failed.RootElement.GetProperty("status").GetString());
        Assert.Equal("磁碟已滿", failed.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void 回覆不含_jobId_前端的進度訂閱不會誤收()
    {
        using var doc = JsonDocument.Parse(SaveFileProtocol.ResultJson("a1", SaveFileProtocol.Cancelled));
        Assert.False(doc.RootElement.TryGetProperty("jobId", out _));
    }
}
