using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SaveIT.Api.Data;

namespace SaveIT.Api.Migrations;

[DbContext(typeof(SaveItDb))]
[Migration("20261109140000_ComputeReleaseAdmission")]
public sealed class ComputeReleaseAdmission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ComputeReleaseSql.Up);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ComputeReleaseSql.Down);
}
