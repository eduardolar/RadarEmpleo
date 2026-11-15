using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class Agentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EjecucionesAgente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Tarea = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ParametrosJson = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Espera = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    QuienBloquea = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FechaAlta = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Inicio = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Fin = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PresupuestoUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    TiempoMaximoSegundos = table.Column<int>(type: "INTEGER", nullable: false),
                    Modelo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CosteUsd = table.Column<decimal>(type: "TEXT", nullable: true),
                    Turnos = table.Column<int>(type: "INTEGER", nullable: true),
                    Resumen = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ResultadoJson = table.Column<string>(type: "TEXT", nullable: true),
                    SesionClaude = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Pid = table.Column<int>(type: "INTEGER", nullable: true),
                    InicioProceso = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RutaLog = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    HashRegistroAntes = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HashRegistroDespues = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HashPerfilAntes = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HashPerfilDespues = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CancelacionPedida = table.Column<bool>(type: "INTEGER", nullable: false),
                    CandidaturaId = table.Column<int>(type: "INTEGER", nullable: true),
                    PreparacionId = table.Column<int>(type: "INTEGER", nullable: true),
                    EnvioId = table.Column<int>(type: "INTEGER", nullable: true),
                    Paso = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EjecucionesAgente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EjecucionesAgente_Candidaturas_CandidaturaId",
                        column: x => x.CandidaturaId,
                        principalTable: "Candidaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EjecucionesAgente_CandidaturaId",
                table: "EjecucionesAgente",
                column: "CandidaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_EjecucionesAgente_Estado",
                table: "EjecucionesAgente",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_EjecucionesAgente_Inicio",
                table: "EjecucionesAgente",
                column: "Inicio");

            migrationBuilder.CreateIndex(
                name: "IX_EjecucionesAgente_Tarea_CandidaturaId",
                table: "EjecucionesAgente",
                columns: new[] { "Tarea", "CandidaturaId" });

            // Hasta ahora no había ejecuciones: un EjecucionAgenteId con valor no apuntaría a nada y haría fallar
            // la clave foránea al guardar ese rastreo. La F5 nunca lo rellenaba, así que no se pierde nada.
            // La clave foránea reconstruye la tabla Rastreos (SQLite): EF copia todas las filas tal cual y,
            // con las claves foráneas apagadas mientras tanto, OfertasRastreo no se borra en cascada.
            migrationBuilder.Sql("""UPDATE "Rastreos" SET "EjecucionAgenteId" = NULL WHERE "EjecucionAgenteId" IS NOT NULL;""");

            migrationBuilder.AddForeignKey(
                name: "FK_Rastreos_EjecucionesAgente_EjecucionAgenteId",
                table: "Rastreos",
                column: "EjecucionAgenteId",
                principalTable: "EjecucionesAgente",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rastreos_EjecucionesAgente_EjecucionAgenteId",
                table: "Rastreos");

            migrationBuilder.DropTable(
                name: "EjecucionesAgente");
        }
    }
}
