using System.Text;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Entities;
using Scheduler.Persistence.Repositories;

namespace Scheduler.Persistence.Seed;

/// <summary>
/// 發佈包第一次啟動時，從名冊檔匯入真實名冊（#82）。名冊檔放在程式旁的 <c>roster/roster.csv</c>
/// （由 <c>build/publish.ps1 -RosterFile</c> 驗證後複製進去），**真實姓名與員編不進版控**。
/// 匯入條件三者同時成立：名冊檔存在、meta 表沒有「已匯入」標記、人員表是空的。
/// 匯入後寫標記，之後不論人員被刪光或檔案還在不在都不再匯入。
/// 名冊檔有問題時不匯入、不丟例外（程式必須能正常啟動），只留一筆警告紀錄，也不寫標記，
/// 換上修好的檔案後下次啟動會再試。
/// </summary>
public static class RosterImporter
{
    public const string ImportedMetaKey = "roster_imported_at";

    /// <summary>發佈包內名冊檔的相對路徑（相對程式資料夾，與 <c>data/</c> 並列：data/ 是執行期狀態，刪掉重來時名冊檔要留著）。</summary>
    public static readonly string RelativePath = Path.Combine("roster", "roster.csv");

    public const string Header = "員編,姓名,身分";

    public static async Task ImportIfFirstRunAsync(
        SchedulerDbContext db, string rosterFilePath, Action<string> warn, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(rosterFilePath))
        {
            return;
        }

        if (await db.AppMeta.AnyAsync(m => m.Key == ImportedMetaKey, cancellationToken))
        {
            return;
        }

        if (await db.Staff.AnyAsync(cancellationToken))
        {
            // 舊資料庫升級：人員表已有人、從沒匯入過。名冊來源已經由使用者決定（#37 情境），
            // 記下標記，免得他日後刪光人員重啟時被名冊檔匯入。
            db.AppMeta.Add(new AppMetaEntity { Key = ImportedMetaKey, Value = now.UtcDateTime.ToString("O") });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var rankCodes = await db.Ranks.Select(r => r.Code).ToListAsync(cancellationToken);
        RosterParseResult result;
        try
        {
            // throwOnInvalidBytes：Big5 等非 UTF-8 檔案要明確失敗，不能默默讀成亂碼
            var text = await File.ReadAllTextAsync(rosterFilePath, new UTF8Encoding(false, true), cancellationToken);
            result = Parse(text, rankCodes);
        }
        catch (DecoderFallbackException)
        {
            warn("名冊檔不是 UTF-8，略過匯入（請用 Excel 另存為「CSV UTF-8（逗號分隔）」）");
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warn($"名冊檔讀不進來，略過匯入：{ex.GetType().Name}");
            return;
        }

        if (result.Errors.Count > 0)
        {
            // 只記列號與原因，不記內容：紀錄檔不該洩漏姓名與員編
            warn("名冊檔驗證不過，略過匯入：" + string.Join("；", result.Errors));
            return;
        }

        var staff = new StaffRepository(db);
        foreach (var member in result.Members)
        {
            await staff.AddAsync(
                new Staff("s-" + Guid.NewGuid().ToString("N")[..12], member.EmployeeNo, member.Name, member.RankCode, StaffStatus.Active),
                cancellationToken);
        }

        db.AppMeta.Add(new AppMetaEntity { Key = ImportedMetaKey, Value = now.UtcDateTime.ToString("O") });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 解析並驗證名冊檔內容：UTF-8（可帶 BOM）、第一列是表頭 <see cref="Header"/>、員編不重複且非空、姓名非空、
    /// 身分代碼必須在 <paramref name="knownRankCodes"/> 內。全空白的列略過；錯誤訊息用實際檔案的列號（表頭是第 1 列）。
    /// 欄位前後的引號會去掉；不處理欄位內含逗號。錯誤訊息只含列號與原因，不含任何欄位原文（紀錄檔不該洩漏姓名與員編）。
    /// </summary>
    public static RosterParseResult Parse(string content, IEnumerable<string> knownRankCodes)
    {
        var ranks = new HashSet<string>(knownRankCodes, StringComparer.Ordinal);
        var errors = new List<string>();
        var members = new List<RosterEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var lines = content.TrimStart((char)0xFEFF).Split('\n');
        var header = lines[0].Replace("\"", "").Replace(" ", "").Trim();
        if (header != Header)
        {
            errors.Add($"第 1 列：表頭必須是「{Header}」");
            return new RosterParseResult(members, errors);
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var fields = lines[i].TrimEnd('\r').Split(',').Select(f => f.Trim().Trim('"').Trim()).ToArray();
            if (fields.All(f => f.Length == 0))
            {
                continue;
            }

            if (fields.Length != 3)
            {
                errors.Add($"第 {lineNo} 列：欄位數必須是 3（員編,姓名,身分），實際是 {fields.Length}");
                continue;
            }

            var (no, name, rank) = (fields[0], fields[1], fields[2]);
            if (no.Length == 0)
            {
                errors.Add($"第 {lineNo} 列：員編是空的");
            }
            else if (!seen.Add(no))
            {
                errors.Add($"第 {lineNo} 列：員編與前面的列重複");
            }

            if (name.Length == 0)
            {
                errors.Add($"第 {lineNo} 列：姓名是空的");
            }

            if (!ranks.Contains(rank))
            {
                // 不印原值：欄位對調時那裡會是姓名，錯誤訊息只含列號與原因
                errors.Add($"第 {lineNo} 列：身分代碼不在已知清單內");
            }

            members.Add(new RosterEntry(no, name, rank));
        }

        if (members.Count == 0 && errors.Count == 0)
        {
            errors.Add("沒有任何人員列");
        }

        return new RosterParseResult(members, errors);
    }
}

public sealed record RosterEntry(string EmployeeNo, string Name, string RankCode);

public sealed record RosterParseResult(IReadOnlyList<RosterEntry> Members, IReadOnlyList<string> Errors);
