using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "area_type",
                columns: table => new
                {
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_area_type", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "blocked_day",
                columns: table => new
                {
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocked_day", x => new { x.staff_id, x.date });
                });

            migrationBuilder.CreateTable(
                name: "calendar_day",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    is_holiday = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_public_holiday = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_make_up_workday = table.Column<bool>(type: "INTEGER", nullable: false),
                    holiday_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    overridden = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_day", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "constraint_definition",
                columns: table => new
                {
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    primitive = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    weight = table.Column<int>(type: "INTEGER", nullable: false),
                    metric = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    param_days = table.Column<int>(type: "INTEGER", nullable: true),
                    param_cap = table.Column<int>(type: "INTEGER", nullable: true),
                    param_direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_constraint_definition", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "eligibility",
                columns: table => new
                {
                    rank_code = table.Column<string>(type: "TEXT", nullable: false),
                    area_type_code = table.Column<string>(type: "TEXT", nullable: false),
                    eligible = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eligibility", x => new { x.rank_code, x.area_type_code });
                });

            migrationBuilder.CreateTable(
                name: "fairness_point_table",
                columns: table => new
                {
                    point_type = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    today = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    tomorrow = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    points = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fairness_point_table", x => new { x.point_type, x.today, x.tomorrow });
                });

            migrationBuilder.CreateTable(
                name: "monthly_override",
                columns: table => new
                {
                    year = table.Column<int>(type: "INTEGER", nullable: false),
                    month = table.Column<int>(type: "INTEGER", nullable: false),
                    rank_code = table.Column<string>(type: "TEXT", nullable: false),
                    quota_cap = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_override", x => new { x.year, x.month, x.rank_code });
                });

            migrationBuilder.CreateTable(
                name: "point_rule",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    quota_weekday = table.Column<int>(type: "INTEGER", nullable: false),
                    quota_holiday = table.Column<int>(type: "INTEGER", nullable: false),
                    saturday_bonus_points = table.Column<int>(type: "INTEGER", nullable: false),
                    saturday_bonus_window_days = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rank_group",
                columns: table => new
                {
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rank_group", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "schedule",
                columns: table => new
                {
                    year = table.Column<int>(type: "INTEGER", nullable: false),
                    month = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    published_at = table.Column<string>(type: "TEXT", maxLength: 28, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule", x => new { x.year, x.month });
                });

            migrationBuilder.CreateTable(
                name: "solver_job",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    year = table.Column<int>(type: "INTEGER", nullable: false),
                    month = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    variant_count = table.Column<int>(type: "INTEGER", nullable: false),
                    time_limit_sec_per_variant = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", maxLength: 28, nullable: false),
                    started_at = table.Column<string>(type: "TEXT", maxLength: 28, nullable: true),
                    finished_at = table.Column<string>(type: "TEXT", maxLength: 28, nullable: true),
                    elapsed_sec = table.Column<double>(type: "REAL", nullable: true),
                    failure_reason = table.Column<string>(type: "TEXT", nullable: true),
                    scale_staff = table.Column<int>(type: "INTEGER", nullable: true),
                    scale_areas = table.Column<int>(type: "INTEGER", nullable: true),
                    scale_days = table.Column<int>(type: "INTEGER", nullable: true),
                    scale_variables = table.Column<int>(type: "INTEGER", nullable: true),
                    hard_constraint_count = table.Column<int>(type: "INTEGER", nullable: true),
                    soft_constraint_count = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solver_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    employee_no = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    rank_code = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "area",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    area_type_code = table.Column<string>(type: "TEXT", nullable: false),
                    required_per_day = table.Column<int>(type: "INTEGER", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_area", x => x.id);
                    table.ForeignKey(
                        name: "FK_area_area_type_area_type_code",
                        column: x => x.area_type_code,
                        principalTable: "area_type",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "constraint_scope_entry",
                columns: table => new
                {
                    constraint_code = table.Column<string>(type: "TEXT", nullable: false),
                    dimension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_constraint_scope_entry", x => new { x.constraint_code, x.dimension, x.value });
                    table.ForeignKey(
                        name: "FK_constraint_scope_entry_constraint_definition_constraint_code",
                        column: x => x.constraint_code,
                        principalTable: "constraint_definition",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rank",
                columns: table => new
                {
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    group_code = table.Column<string>(type: "TEXT", nullable: false),
                    quota_cap = table.Column<int>(type: "INTEGER", nullable: true),
                    point_type = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rank", x => x.code);
                    table.ForeignKey(
                        name: "FK_rank_rank_group_group_code",
                        column: x => x.group_code,
                        principalTable: "rank_group",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "carry_over",
                columns: table => new
                {
                    year = table.Column<int>(type: "INTEGER", nullable: false),
                    month = table.Column<int>(type: "INTEGER", nullable: false),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    points = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carry_over", x => new { x.year, x.month, x.staff_id });
                    table.ForeignKey(
                        name: "FK_carry_over_schedule_year_month",
                        columns: x => new { x.year, x.month },
                        principalTable: "schedule",
                        principalColumns: new[] { "year", "month" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "carry_over_applied",
                columns: table => new
                {
                    year = table.Column<int>(type: "INTEGER", nullable: false),
                    month = table.Column<int>(type: "INTEGER", nullable: false),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false),
                    points = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carry_over_applied", x => new { x.year, x.month, x.staff_id });
                    table.ForeignKey(
                        name: "FK_carry_over_applied_schedule_year_month",
                        columns: x => new { x.year, x.month },
                        principalTable: "schedule",
                        principalColumns: new[] { "year", "month" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "duty",
                columns: table => new
                {
                    year = table.Column<int>(type: "INTEGER", nullable: false),
                    month = table.Column<int>(type: "INTEGER", nullable: false),
                    area_id = table.Column<string>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duty", x => new { x.year, x.month, x.area_id, x.date });
                    table.ForeignKey(
                        name: "FK_duty_schedule_year_month",
                        columns: x => new { x.year, x.month },
                        principalTable: "schedule",
                        principalColumns: new[] { "year", "month" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "solver_job_warning",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    seq = table.Column<int>(type: "INTEGER", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solver_job_warning", x => new { x.job_id, x.seq });
                    table.ForeignKey(
                        name: "FK_solver_job_warning_solver_job_job_id",
                        column: x => x.job_id,
                        principalTable: "solver_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variant",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    label = table.Column<string>(type: "TEXT", nullable: false),
                    hard_violation_count = table.Column<int>(type: "INTEGER", nullable: false),
                    soft_score = table.Column<double>(type: "REAL", nullable: false),
                    metric_vacancies = table.Column<int>(type: "INTEGER", nullable: false),
                    metric_quota_fairness = table.Column<double>(type: "REAL", nullable: false),
                    metric_area_consistency = table.Column<double>(type: "REAL", nullable: false),
                    metric_rank_preference = table.Column<double>(type: "REAL", nullable: false),
                    metric_fairness_point = table.Column<double>(type: "REAL", nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant", x => new { x.job_id, x.id });
                    table.ForeignKey(
                        name: "FK_variant_solver_job_job_id",
                        column: x => x.job_id,
                        principalTable: "solver_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variant_duty",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    area_id = table.Column<string>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    staff_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant_duty", x => new { x.job_id, x.variant_id, x.area_id, x.date });
                    table.ForeignKey(
                        name: "FK_variant_duty_variant_job_id_variant_id",
                        columns: x => new { x.job_id, x.variant_id },
                        principalTable: "variant",
                        principalColumns: new[] { "job_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variant_weight",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    variant_id = table.Column<string>(type: "TEXT", nullable: false),
                    constraint_code = table.Column<string>(type: "TEXT", nullable: false),
                    multiplier = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant_weight", x => new { x.job_id, x.variant_id, x.constraint_code });
                    table.ForeignKey(
                        name: "FK_variant_weight_variant_job_id_variant_id",
                        columns: x => new { x.job_id, x.variant_id },
                        principalTable: "variant",
                        principalColumns: new[] { "job_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_area_area_type_code",
                table: "area",
                column: "area_type_code");

            migrationBuilder.CreateIndex(
                name: "IX_area_code",
                table: "area",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_blocked_day_date",
                table: "blocked_day",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_duty_date",
                table: "duty",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_duty_staff_id",
                table: "duty",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "IX_rank_group_code",
                table: "rank",
                column: "group_code");

            migrationBuilder.CreateIndex(
                name: "IX_solver_job_created_at",
                table: "solver_job",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_staff_employee_no",
                table: "staff",
                column: "employee_no",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "area");

            migrationBuilder.DropTable(
                name: "blocked_day");

            migrationBuilder.DropTable(
                name: "calendar_day");

            migrationBuilder.DropTable(
                name: "carry_over");

            migrationBuilder.DropTable(
                name: "carry_over_applied");

            migrationBuilder.DropTable(
                name: "constraint_scope_entry");

            migrationBuilder.DropTable(
                name: "duty");

            migrationBuilder.DropTable(
                name: "eligibility");

            migrationBuilder.DropTable(
                name: "fairness_point_table");

            migrationBuilder.DropTable(
                name: "monthly_override");

            migrationBuilder.DropTable(
                name: "point_rule");

            migrationBuilder.DropTable(
                name: "rank");

            migrationBuilder.DropTable(
                name: "solver_job_warning");

            migrationBuilder.DropTable(
                name: "staff");

            migrationBuilder.DropTable(
                name: "variant_duty");

            migrationBuilder.DropTable(
                name: "variant_weight");

            migrationBuilder.DropTable(
                name: "area_type");

            migrationBuilder.DropTable(
                name: "constraint_definition");

            migrationBuilder.DropTable(
                name: "schedule");

            migrationBuilder.DropTable(
                name: "rank_group");

            migrationBuilder.DropTable(
                name: "variant");

            migrationBuilder.DropTable(
                name: "solver_job");
        }
    }
}
