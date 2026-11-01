using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class FechaEstado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEstado",
                table: "Candidaturas",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Filas existentes, igual que haría Candidatura hoy: entraron en su estado actual con el último
            // cambio de estado posterior al alta (el más reciente por fecha y, a igualdad, por id; el alta es
            // el apunte con "De" a NULL). Si solo tienen el alta, siguen en «Detectada» desde la detección.
            // Las fechas se guardan como texto con el mismo formato, así que ordenar el texto es ordenar la fecha.
            migrationBuilder.Sql("""
                UPDATE "Candidaturas"
                SET "FechaEstado" = COALESCE(
                    (SELECT h."Fecha"
                     FROM "CambiosEstado" AS h
                     WHERE h."CandidaturaId" = "Candidaturas"."Id" AND h."De" IS NOT NULL
                     ORDER BY h."Fecha" DESC, h."Id" DESC
                     LIMIT 1),
                    "FechaDeteccion");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaEstado",
                table: "Candidaturas");
        }
    }
}
