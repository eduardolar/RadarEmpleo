using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Rastreos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Rastreos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Clave = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Origen = table.Column<string>(type: "TEXT", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Fecha = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RutaInforme = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TextoInforme = table.Column<string>(type: "TEXT", nullable: false),
                    FechaEscaneo = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EjecucionAgenteId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rastreos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfertasRastreo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RastreoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Linea = table.Column<int>(type: "INTEGER", nullable: false),
                    TextoOriginal = table.Column<string>(type: "TEXT", nullable: false),
                    Empresa = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Puesto = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Enlace = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Encaje = table.Column<string>(type: "TEXT", nullable: false),
                    Pega = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Estado = table.Column<string>(type: "TEXT", nullable: false),
                    CandidaturaId = table.Column<int>(type: "INTEGER", nullable: true),
                    FechaEstado = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfertasRastreo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfertasRastreo_Candidaturas_CandidaturaId",
                        column: x => x.CandidaturaId,
                        principalTable: "Candidaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OfertasRastreo_Rastreos_RastreoId",
                        column: x => x.RastreoId,
                        principalTable: "Rastreos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfertasRastreo_CandidaturaId",
                table: "OfertasRastreo",
                column: "CandidaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_OfertasRastreo_Enlace",
                table: "OfertasRastreo",
                column: "Enlace",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfertasRastreo_RastreoId",
                table: "OfertasRastreo",
                column: "RastreoId");

            migrationBuilder.CreateIndex(
                name: "IX_Rastreos_Clave",
                table: "Rastreos",
                column: "Clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rastreos_EjecucionAgenteId",
                table: "Rastreos",
                column: "EjecucionAgenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Rastreos_Fecha",
                table: "Rastreos",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfertasRastreo");

            migrationBuilder.DropTable(
                name: "Rastreos");
        }
    }
}
