using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Scheduler.Api.Contracts;
using Scheduler.Application.Solving;

namespace Scheduler.Api.Solving;

/// <summary>
/// 求解進度的事件來源，Shell 從 <c>app.Services</c> 取的就是這個（ARCHITECTURE §6.2）。
/// 介面只用 BCL 型別：Shell 只引用 Api（硬性規則 2），簽章裡出現 Application 型別會讓 Shell 編譯不過。
/// 每筆字串都是契約 <c>SolverProgress</c> 的 JSON，SSE 端點寫進 <c>data:</c> 的也是同一個方法產的，
/// 兩個 transport 收到的內容從構造上就相同。
/// </summary>
public interface ISolverProgressFeed
{
    /// <summary>所有工作的進度事件，每筆都帶 <c>jobId</c>，不會自己結束。給只有一條 PostWebMessageAsJson 通道的 Shell。</summary>
    IAsyncEnumerable<string> AllAsync(CancellationToken cancellationToken);

    /// <summary>單一工作的進度事件，工作結束時序列結束；工作不存在擲 404。給 SSE 端點。</summary>
    IAsyncEnumerable<string> ForJobAsync(string jobId, CancellationToken cancellationToken);
}

internal sealed class SolverProgressFeed : ISolverProgressFeed
{
    private readonly SolverJobService _jobs;
    private readonly JsonSerializerOptions _json;

    public SolverProgressFeed(SolverJobService jobs, IOptions<JsonOptions> json)
    {
        _jobs = jobs;
        _json = json.Value.SerializerOptions;
    }

    public async IAsyncEnumerable<string> AllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var snapshot in _jobs.SubscribeAllAsync(cancellationToken))
        {
            yield return Serialize(snapshot);
        }
    }

    public async IAsyncEnumerable<string> ForJobAsync(string jobId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var snapshot in _jobs.SubscribeAsync(jobId, cancellationToken))
        {
            yield return Serialize(snapshot);
        }
    }

    private string Serialize(SolverProgressSnapshot snapshot) => JsonSerializer.Serialize(snapshot.ToContract(), _json);
}
