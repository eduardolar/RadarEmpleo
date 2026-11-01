using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Empresas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    NombreNormalizado = table.Column<string>(type: "TEXT", nullable: false),
                    Web = table.Column<string>(type: "TEXT", nullable: true),
                    Notas = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Candidaturas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Puesto = table.Column<string>(type: "TEXT", nullable: false),
                    Enlace = table.Column<string>(type: "TEXT", nullable: true),
                    Portal = table.Column<string>(type: "TEXT", nullable: true),
                    Modalidad = table.Column<string>(type: "TEXT", nullable: false),
                    ModalidadDetalle = table.Column<string>(type: "TEXT", nullable: true),
                    Ubicacion = table.Column<string>(type: "TEXT", nullable: true),
                    Encaje = table.Column<string>(type: "TEXT", nullable: false),
                    Pega = table.Column<string>(type: "TEXT", nullable: true),
                    Analisis = table.Column<string>(type: "TEXT", nullable: true),
                    Salario = table.Column<string>(type: "TEXT", nullable: true),
                    Estado = table.Column<string>(type: "TEXT", nullable: false),
                    FechaDeteccion = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FechaEnvio = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FechaUltimoCambio = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MotivoDescarte = table.Column<string>(type: "TEXT", nullable: true),
                    MotivoDescarteTexto = table.Column<string>(type: "TEXT", nullable: true),
                    Origen = table.Column<string>(type: "TEXT", nullable: false),
                    ClaveImportacion = table.Column<string>(type: "TEXT", nullable: true),
                    SeccionOrigen = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidaturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candidaturas_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CambiosEstado",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidaturaId = table.Column<int>(type: "INTEGER", nullable: false),
                    De = table.Column<string>(type: "TEXT", nullable: true),
                    A = table.Column<string>(type: "TEXT", nullable: false),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Comentario = table.Column<string>(type: "TEXT", nullable: true),
                    Origen = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CambiosEstado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CambiosEstado_Candidaturas_CandidaturaId",
                        column: x => x.CandidaturaId,
                        principalTable: "Candidaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidaturaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Texto = table.Column<string>(type: "TEXT", nullable: false),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Origen = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notas_Candidaturas_CandidaturaId",
                        column: x => x.CandidaturaId,
                        principalTable: "Candidaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recordatorios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidaturaId = table.Column<int>(type: "INTEGER", nullable: true),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", nullable: false),
                    FechaHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Hecho = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notas = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordatorios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recordatorios_Candidaturas_CandidaturaId",
                        column: x => x.CandidaturaId,
                        principalTable: "Candidaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CambiosEstado_CandidaturaId",
                table: "CambiosEstado",
                column: "CandidaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidaturas_ClaveImportacion",
                table: "Candidaturas",
                column: "ClaveImportacion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Candidaturas_EmpresaId",
                table: "Candidaturas",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidaturas_Estado",
                table: "Candidaturas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_NombreNormalizado",
                table: "Empresas",
                column: "NombreNormalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notas_CandidaturaId",
                table: "Notas",
                column: "CandidaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_Recordatorios_CandidaturaId",
                table: "Recordatorios",
                column: "CandidaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_Recordatorios_FechaHora",
                table: "Recordatorios",
                column: "FechaHora");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CambiosEstado");

            migrationBuilder.DropTable(
                name: "Notas");

            migrationBuilder.DropTable(
                name: "Recordatorios");

            migrationBuilder.DropTable(
                name: "Candidaturas");

            migrationBuilder.DropTable(
                name: "Empresas");
        }
    }
}
