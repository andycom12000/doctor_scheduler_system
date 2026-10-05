using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Persistence.Seed;

/// <summary>
/// 出廠參考名單：33 位醫師 + 1 位 NP = 34 人。姓名為假名、員編 <c>E001…E034</c>，
/// 組成、順序與姓名照 <c>frontend/src/mocks/fixtures/staff.ts</c> 抄，兩邊的
/// <c>npm run mock:smoke</c>／<c>npm run api:smoke</c> 才能共用同一份斷言。
/// 人數組成照 <see cref="DefaultRanks.ReferenceHeadcount"/>（唯一來源）。
/// 是否種由 <c>ApiHostOptions.SeedReferenceRoster</c> 決定，發佈包關閉（#37）；發佈包改從名冊檔匯入真實名冊（#82，見 <see cref="RosterImporter"/>）。
/// </summary>
public static class ReferenceRoster
{
    private static readonly (string RankCode, string Name)[] Seeds =
    {
        (DefaultRanks.PGY1, "陳建宏"),
        (DefaultRanks.PGY1, "林俊傑"),
        (DefaultRanks.PGY2, "黃冠廷"),
        (DefaultRanks.PGY2, "張承翰"),
        (DefaultRanks.PGY2, "李柏翰"),
        (DefaultRanks.PGY2, "王品睿"),
        (DefaultRanks.R1, "吳宇軒"),
        (DefaultRanks.R1, "劉冠宇"),
        (DefaultRanks.R1, "蔡育誠"),
        (DefaultRanks.R1, "楊家豪"),
        (DefaultRanks.R2, "許志明"),
        (DefaultRanks.R2, "鄭俊宏"),
        (DefaultRanks.R2, "謝彥廷"),
        (DefaultRanks.R3, "洪冠霖"),
        (DefaultRanks.R3, "郭柏宇"),
        (DefaultRanks.R3, "曾詩涵"),
        (DefaultRanks.R3, "廖雅婷"),
        (DefaultRanks.R4, "賴怡君"),
        (DefaultRanks.R4, "徐淑芬"),
        (DefaultRanks.R4, "周佳蓉"),
        (DefaultRanks.R4, "葉淑惠"),
        (DefaultRanks.R5, "蘇美玲"),
        (DefaultRanks.R5, "莊靜怡"),
        (DefaultRanks.R5, "呂雅雯"),
        (DefaultRanks.R5, "江惠婷"),
        (DefaultRanks.R5, "何心怡"),
        (DefaultRanks.R6, "蕭佩珊"),
        (DefaultRanks.R6, "羅郁婷"),
        (DefaultRanks.R6, "高思妤"),
        (DefaultRanks.R6, "潘芳瑜"),
        (DefaultRanks.PTR, "簡婉婷"),
        (DefaultRanks.PTR, "朱姿穎"),
        (DefaultRanks.PTR, "鍾品妤"),
        (DefaultRanks.NP, "游芷若"),
    };

    /// <summary>
    /// 每次呼叫都是新的一組 id（<c>Guid</c>，沿用 <c>StaffCommands.NewId</c> 的做法）——
    /// 靜態欄位快取會讓同一個 process 裡的多顆測試資料庫共用同一批 id，這裡刻意不快取。
    /// </summary>
    public static IReadOnlyList<Staff> Build()
    {
        var result = new List<Staff>(Seeds.Length);
        for (var i = 0; i < Seeds.Length; i++)
        {
            var (rankCode, name) = Seeds[i];
            var employeeNo = $"E{(i + 1):D3}";
            result.Add(new Staff("s-" + Guid.NewGuid().ToString("N")[..12], employeeNo, name, rankCode, StaffStatus.Active));
        }

        return result;
    }
}
