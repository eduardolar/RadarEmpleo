using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Documentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidaturaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    NombreArchivo = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    RutaRelativa = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Encontrado = table.Column<bool>(type: "INTEGER", nullable: false),
                    Origen = table.Column<string>(type: "TEXT", nullable: false),
                    PreparacionId = table.Column<int>(type: "INTEGER", nullable: true),
                    Vigente = table.Column<bool>(type: "INTEGER", nullable: false),
                    HashSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Paginas = table.Column<int>(type: "INTEGER", nullable: true),
                    FechaAlta = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documentos_Candidaturas_CandidaturaId",
                        column: x => x.CandidaturaId,
                        principalTable: "Candidaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_CandidaturaId_RutaRelativa",
                table: "Documentos",
                columns: new[] { "CandidaturaId", "RutaRelativa" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_CandidaturaId_Tipo_Vigente",
                table: "Documentos",
                columns: new[] { "CandidaturaId", "Tipo" },
                unique: true,
                filter: "\"Vigente\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_PreparacionId",
                table: "Documentos",
                column: "PreparacionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documentos");
        }
    }
}
