using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SolverJobLastSearchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "last_best_bound",
                table: "solver_job",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "last_best_objective",
                table: "solver_job",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_solution_count",
                table: "solver_job",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_variant_index",
                table: "solver_job",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_best_bound",
                table: "solver_job");

            migrationBuilder.DropColumn(
                name: "last_best_objective",
                table: "solver_job");

            migrationBuilder.DropColumn(
                name: "last_solution_count",
                table: "solver_job");

            migrationBuilder.DropColumn(
                name: "last_variant_index",
                table: "solver_job");
        }
    }
}
