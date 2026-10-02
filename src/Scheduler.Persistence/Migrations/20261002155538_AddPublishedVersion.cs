using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "published_version",
                table: "schedule",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // 回填：已發布的月份視為已定版過一次（v1），草稿維持 0（從未發布）。
            migrationBuilder.Sql("UPDATE schedule SET published_version = 1 WHERE status = 'published';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "published_version",
                table: "schedule");
        }
    }
}
