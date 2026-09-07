using System.Text.Json.Nodes;

namespace Scheduler.Api.Tests;

/// <summary>
/// 守住驗證器本身：若 yaml → schema 的轉換壞掉（純量型別、$ref 改寫、format 檢查），
/// 守法測試會變成什麼都過。這裡用故意錯的本體確認它真的會擋。
/// </summary>
public sealed class ContractSchemaTests
{
    [Fact]
    public void 缺必要欄位會被擋()
    {
        var errors = ContractSchema.Current.Validate("getSchedule", 200, JsonNode.Parse("{}"));
        Assert.Contains(errors, e => e.Contains("required"));
    }

    [Fact]
    public void ref_到元件的巢狀結構有被驗到()
    {
        var body = JsonNode.Parse("""
            {"yearMonth":"2026-09","status":"draft","revision":1,"dayCount":30,"areas":[{"id":"a"}],"duties":[]}
            """);
        var errors = ContractSchema.Current.Validate("getSchedule", 200, body);
        Assert.Contains(errors, e => e.StartsWith("/areas/0"));
    }

    [Fact]
    public void 列舉字串與null型別有被驗到()
    {
        var ok = JsonNode.Parse("""{"error":{"code":"NOT_FOUND","message":"x"}}""");
        Assert.Empty(ContractSchema.Current.ValidateComponent("ErrorResponse", ok));

        var badCode = JsonNode.Parse("""{"error":{"code":"NotFound","message":"x"}}""");
        Assert.NotEmpty(ContractSchema.Current.ValidateComponent("ErrorResponse", badCode));

        var np = JsonNode.Parse("""{"code":"NP","name":"NP","groupCode":"np","quotaCap":null,"pointType":null}""");
        Assert.Empty(ContractSchema.Current.ValidateComponent("Rank", np));

        var missingCap = JsonNode.Parse("""{"code":"NP","name":"NP","groupCode":"np","pointType":null}""");
        Assert.NotEmpty(ContractSchema.Current.ValidateComponent("Rank", missingCap));
    }

    [Fact]
    public void 未知欄位會被擋_欄名打錯才驗得到()
    {
        var typo = JsonNode.Parse("""{"areaId":"a","date":"2026-09-01","staffId":"s","cellKeyz":"x"}""");
        Assert.Contains(ContractSchema.Current.ValidateComponent("Duty", typo), e => e.Contains("additionalProperties"));
    }

    [Fact]
    public void 回應層級的ref解得開()
    {
        var body = JsonNode.Parse("""{"error":{"code":"NOT_FOUND","message":"x"}}""");
        Assert.Empty(ContractSchema.Current.Validate("getSchedule", 404, body));
    }

    [Fact]
    public void 日期格式有被驗到()
    {
        var bad = JsonNode.Parse("""{"areaId":"a","date":"2026/09/01","staffId":"s"}""");
        Assert.NotEmpty(ContractSchema.Current.ValidateComponent("Duty", bad));

        var good = JsonNode.Parse("""{"areaId":"a","date":"2026-09-01","staffId":"s"}""");
        Assert.Empty(ContractSchema.Current.ValidateComponent("Duty", good));
    }

    [Fact]
    public void yaml的純量有推斷型別()
    {
        // required: true 若變成字串 "true"，schema 會整個壞掉；minimum: 0 同理。
        var json = YamlToJson.Load("a: true\nb: 0\nc: '200'\nd: [string, 'null']\ne: null\nf: 1.5\n")!;
        Assert.True(json["a"]!.GetValue<bool>());
        Assert.Equal(0, json["b"]!.GetValue<long>());
        Assert.Equal("200", json["c"]!.GetValue<string>());
        Assert.Equal("null", json["d"]![1]!.GetValue<string>());
        Assert.Null(json["e"]);
        Assert.Equal(1.5, json["f"]!.GetValue<double>());
    }
}
