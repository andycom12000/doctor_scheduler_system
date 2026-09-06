using Scheduler.Domain.Model;

namespace Scheduler.Application.Settings;

/// <summary><c>GET · PUT /api/settings/areas</c> 讀寫的整份文件。順序有意義，讀回來與寫進去的順序相同。</summary>
public sealed record AreaSettings(IReadOnlyList<AreaType> AreaTypes, IReadOnlyList<Area> Areas);

/// <summary><c>GET · PUT /api/settings/ranks</c> 讀寫的整份文件。順序有意義。</summary>
public sealed record RankSettings(IReadOnlyList<RankGroup> Groups, IReadOnlyList<Rank> Ranks);
