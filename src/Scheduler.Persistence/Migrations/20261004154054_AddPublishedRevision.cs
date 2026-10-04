using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "published_revision",
                table: "schedule",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // 既有的已發布資料不知道發布後有沒有再改過，當作沒改（以目前的 revision 回填），
            // 否則升級後每個已發布月份都會被標成「發布後有修改」（#75）。
            migrationBuilder.Sql("UPDATE schedule SET published_revision = revision WHERE status = 'published';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "published_revision",
                table: "schedule");
        }
    }
}
