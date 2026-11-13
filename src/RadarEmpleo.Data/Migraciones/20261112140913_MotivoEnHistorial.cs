using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class MotivoEnHistorial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotivoDescarte",
                table: "CambiosEstado",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoDescarteTexto",
                table: "CambiosEstado",
                type: "TEXT",
                nullable: true);

            // Las que ya están descartadas: su último apunte (el descarte) se queda con el motivo de la
            // candidatura, para poder volver a Descartada si se deshace el cambio que la saque de ahí. Del
            // resto de descartes antiguos no se sabe el motivo. Ordenar el texto de la fecha es ordenar la fecha.
            migrationBuilder.Sql("""
                UPDATE "CambiosEstado"
                SET "MotivoDescarte" = (SELECT c."MotivoDescarte" FROM "Candidaturas" AS c WHERE c."Id" = "CambiosEstado"."CandidaturaId"),
                    "MotivoDescarteTexto" = (SELECT c."MotivoDescarteTexto" FROM "Candidaturas" AS c WHERE c."Id" = "CambiosEstado"."CandidaturaId")
                WHERE "A" = 'Descartada'
                  AND EXISTS (SELECT 1 FROM "Candidaturas" AS c
                              WHERE c."Id" = "CambiosEstado"."CandidaturaId" AND c."Estado" = 'Descartada')
                  AND "Id" = (SELECT h."Id" FROM "CambiosEstado" AS h
                              WHERE h."CandidaturaId" = "CambiosEstado"."CandidaturaId"
                              ORDER BY h."Fecha" DESC, h."Id" DESC
                              LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MotivoDescarte",
                table: "CambiosEstado");

            migrationBuilder.DropColumn(
                name: "MotivoDescarteTexto",
                table: "CambiosEstado");
        }
    }
}
