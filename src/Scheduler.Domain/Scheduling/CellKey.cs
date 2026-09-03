using Scheduler.Domain.Model;

namespace Scheduler.Domain.Scheduling;

/// <summary>
/// 前端索引格子用的字串。區域 × 日檢視用 <c>area:{areaId}:{date}</c>，
/// 日 × 人檢視用 <c>staff:{staffId}:{date}</c>。是渲染層的索引鍵，不是領域識別子。
/// </summary>
public static class CellKey
{
    public static string Area(string areaId, DateOnly date) => $"area:{areaId}:{date:yyyy-MM-dd}";

    public static string Area(Duty duty) => Area(duty.AreaId, duty.Date);

    public static string Staff(string staffId, DateOnly date) => $"staff:{staffId}:{date:yyyy-MM-dd}";

    public static string Staff(Duty duty) => Staff(duty.StaffId, duty.Date);
}
