using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class RecordatoriosHoy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaHecho",
                table: "Recordatorios",
                type: "TEXT",
                nullable: true);

            // Desde la F6, pasar a Entrevista u Oferta da por hecho el seguimiento pendiente: se hace lo mismo
            // con las candidaturas que ya estaban ahí. FechaHecho se queda en NULL porque no se sabe cuándo
            // pasó. Este cierre no se puede deshacer: Down solo quita la columna.
            migrationBuilder.Sql("""
                UPDATE "Recordatorios" SET "Hecho" = 1
                WHERE "Tipo" = 'Seguimiento' AND "Hecho" = 0
                  AND "CandidaturaId" IN (SELECT "Id" FROM "Candidaturas" WHERE "Estado" IN ('Entrevista', 'Oferta'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaHecho",
                table: "Recordatorios");
        }
    }
}
