using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Scheduler.Api.Tests;

/// <summary>
/// 把 <c>api-contract.yaml</c> 變成可驗證的 JSON Schema。
/// 每個 (operationId, 狀態碼) 的回應 schema 被抽出來當根 schema，<c>components/schemas</c> 全部搬到
/// <c>$defs</c>、<c>$ref</c> 字串跟著改寫——不依賴驗證器懂 OpenAPI 文件結構。
/// <c>format: date / date-time</c> 的檢查是 opt-in，這裡開著，日期欄位才真的有驗到。
/// </summary>
public sealed class ContractSchema
{
    private static readonly Lazy<ContractSchema> Instance = new(() => new ContractSchema(Path.Combine(AppContext.BaseDirectory, "api-contract.yaml")));

    private readonly JsonObject _document;
    private readonly JsonObject _defs;
    private readonly Dictionary<(string OperationId, int Status), JsonSchema> _cache = new();

    private ContractSchema(string path)
    {
        _document = (JsonObject)YamlToJson.Load(File.ReadAllText(path))!;
        var schemas = (JsonObject)_document["components"]!["schemas"]!;
        _defs = (JsonObject)RewriteRefs(schemas.DeepClone());
    }

    public static ContractSchema Current => Instance.Value;

    /// <summary>契約裡所有操作的 (小寫 HTTP 方法, 路徑)。</summary>
    public IEnumerable<(string Method, string Path)> Operations()
    {
        foreach (var (path, pathItem) in (JsonObject)_document["paths"]!)
        {
            foreach (var (method, _) in (JsonObject)pathItem!)
            {
                if (method != "parameters")
                {
                    yield return (method.ToLowerInvariant(), path);
                }
            }
        }
    }

    public bool HasResponse(string operationId, int status) =>
        FindOperation(operationId)["responses"]?[status.ToString(CultureInfo.InvariantCulture)] is not null;

    /// <summary>契約裡某個操作、某個狀態碼的 JSON 回應 schema。找不到即測試寫錯，直接擲例外。</summary>
    public JsonSchema ResponseSchema(string operationId, int status)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue((operationId, status), out var cached))
            {
                return cached;
            }

            var operation = FindOperation(operationId);
            var response = operation["responses"]?[status.ToString(CultureInfo.InvariantCulture)]
                ?? throw new InvalidOperationException($"契約的 {operationId} 沒有定義 {status} 回應");
            // 回應層級也可能是 $ref（例：'#/components/responses/NotFound'），先解一層。
            if (response["$ref"]?.GetValue<string>() is { } responseRef)
            {
                const string prefix = "#/components/responses/";
                if (!responseRef.StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"不支援的回應 $ref：{responseRef}");
                }

                response = _document["components"]!["responses"]![responseRef[prefix.Length..]]
                    ?? throw new InvalidOperationException($"契約裡沒有 {responseRef}");
            }
            var schema = response["content"]?["application/json"]?["schema"]
                ?? throw new InvalidOperationException($"契約的 {operationId} {status} 沒有 JSON schema");

            var root = (JsonObject)RewriteRefs(schema.DeepClone());
            root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
            root["$defs"] = _defs.DeepClone();

            var built = JsonSchema.FromText(root.ToJsonString());
            _cache[(operationId, status)] = built;
            return built;
        }
    }

    /// <summary><c>components/schemas</c> 裡某個 schema 單獨當根。錯誤回應的形狀對所有端點都一樣，用這個驗。</summary>
    public JsonSchema ComponentSchema(string name)
    {
        lock (_cache)
        {
            var key = ("#" + name, 0);
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (!_defs.ContainsKey(name))
            {
                throw new InvalidOperationException($"契約裡沒有 schema {name}");
            }

            var root = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$ref"] = "#/$defs/" + name,
                ["$defs"] = _defs.DeepClone(),
            };
            var built = JsonSchema.FromText(root.ToJsonString());
            _cache[key] = built;
            return built;
        }
    }

    /// <summary>驗證一段 JSON；不合時回傳可讀的錯誤清單，合則回空。</summary>
    public IReadOnlyList<string> Validate(string operationId, int status, JsonNode? instance) =>
        Validate(ResponseSchema(operationId, status), instance);

    public IReadOnlyList<string> ValidateComponent(string name, JsonNode? instance) =>
        Validate(ComponentSchema(name), instance);

    private static IReadOnlyList<string> Validate(JsonSchema schema, JsonNode? instance)
    {
        // JsonSchema.Net 9 只收 JsonElement；null 本體是 JSON 的 null 字面值。
        using var doc = JsonDocument.Parse(instance?.ToJsonString() ?? "null");
        var results = schema.Evaluate(doc.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });

        if (results.IsValid)
        {
            return Array.Empty<string>();
        }

        var errors = (results.Details ?? Enumerable.Empty<EvaluationResults>())
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} — {e.Value}"))
            .ToList();
        if (errors.Count == 0)
        {
            // 判定與明細是兩個東西：不合但撈不到明細時也不能放行。
            errors.Add("schema 驗證失敗，但驗證器沒有回報明細");
        }

        return errors;
    }

    private JsonNode FindOperation(string operationId)
    {
        foreach (var (_, pathItem) in (JsonObject)_document["paths"]!)
        {
            foreach (var (method, operation) in (JsonObject)pathItem!)
            {
                if (method == "parameters")
                {
                    continue;
                }

                if (operation?["operationId"]?.GetValue<string>() == operationId)
                {
                    return operation;
                }
            }
        }

        throw new InvalidOperationException($"契約裡沒有 operationId {operationId}");
    }

    /// <summary>
    /// 遞迴改寫兩件事：<c>#/components/schemas/X</c> → <c>#/$defs/X</c>；
    /// 有 <c>properties</c> 又沒明寫 <c>additionalProperties</c> 的物件補上 <c>additionalProperties: false</c>。
    /// 後者是守備力的關鍵——契約沒有任何 <c>additionalProperties: false</c>，JSON Schema 預設放行未知屬性，
    /// 不補的話 DTO 欄名打錯只要不在 <c>required</c> 裡就驗不到。
    /// </summary>
    private static JsonNode RewriteRefs(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.ContainsKey("properties") && !obj.ContainsKey("additionalProperties"))
                {
                    obj["additionalProperties"] = false;
                }

                foreach (var key in obj.Select(kv => kv.Key).ToArray())
                {
                    var value = obj[key];
                    if (key == "$ref" && value is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        obj[key] = s.Replace("#/components/schemas/", "#/$defs/", StringComparison.Ordinal);
                    }
                    else if (value is not null)
                    {
                        RewriteRefs(value);
                    }
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not null)
                    {
                        RewriteRefs(item);
                    }
                }

                break;
        }

        return node;
    }
}

/// <summary>
/// YAML → JsonNode。YamlDotNet 的 Deserialize&lt;object&gt; 會把所有純量都變字串
/// （<c>required: true</c> 變 "true"、<c>minimum: 0</c> 變 "0"），schema 就壞了；
/// 這裡自己做最小的型別推斷：只有 plain 純量才推斷 null / bool / int / float，有引號的一律是字串。
/// </summary>
internal static class YamlToJson
{
    public static JsonNode? Load(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return Convert(stream.Documents[0].RootNode);
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode map => ConvertMap(map),
        YamlSequenceNode seq => new JsonArray(seq.Children.Select(Convert).ToArray()),
        YamlScalarNode scalar => ConvertScalar(scalar),
        _ => throw new NotSupportedException($"不支援的 YAML 節點：{node.GetType().Name}"),
    };

    private static JsonObject ConvertMap(YamlMappingNode map)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in map.Children)
        {
            obj[((YamlScalarNode)key).Value!] = Convert(value);
        }

        return obj;
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(text);
        }

        switch (text)
        {
            case "" or "~" or "null":
                return null;
            case "true":
                return JsonValue.Create(true);
            case "false":
                return JsonValue.Create(false);
        }

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
        {
            return JsonValue.Create(l);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return JsonValue.Create(d);
        }

        return JsonValue.Create(text);
    }
}
