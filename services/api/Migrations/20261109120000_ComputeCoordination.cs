using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SaveIT.Api.Data;

namespace SaveIT.Api.Migrations;

// Operational coordination is deliberately outside the tenant EF entity model.
[DbContext(typeof(SaveItDb))]
[Migration("20261109120000_ComputeCoordination")]
public sealed class ComputeCoordination : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ComputeSql.Up);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ComputeSql.Down);
}
