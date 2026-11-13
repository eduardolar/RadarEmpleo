using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class SeguimientosEnHistorial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Qué seguimiento creó o cerró cada cambio, para deshacerlo con exactitud. Los apuntes antiguos se
            // quedan sin él: solo se deshace lo de los últimos 10 minutos.
            migrationBuilder.AddColumn<int>(
                name: "SeguimientoCerradoId",
                table: "CambiosEstado",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeguimientoCreadoId",
                table: "CambiosEstado",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CambiosEstado_SeguimientoCerradoId",
                table: "CambiosEstado",
                column: "SeguimientoCerradoId");

            migrationBuilder.CreateIndex(
                name: "IX_CambiosEstado_SeguimientoCreadoId",
                table: "CambiosEstado",
                column: "SeguimientoCreadoId");

            migrationBuilder.AddForeignKey(
                name: "FK_CambiosEstado_Recordatorios_SeguimientoCerradoId",
                table: "CambiosEstado",
                column: "SeguimientoCerradoId",
                principalTable: "Recordatorios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CambiosEstado_Recordatorios_SeguimientoCreadoId",
                table: "CambiosEstado",
                column: "SeguimientoCreadoId",
                principalTable: "Recordatorios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CambiosEstado_Recordatorios_SeguimientoCerradoId",
                table: "CambiosEstado");

            migrationBuilder.DropForeignKey(
                name: "FK_CambiosEstado_Recordatorios_SeguimientoCreadoId",
                table: "CambiosEstado");

            migrationBuilder.DropIndex(
                name: "IX_CambiosEstado_SeguimientoCerradoId",
                table: "CambiosEstado");

            migrationBuilder.DropIndex(
                name: "IX_CambiosEstado_SeguimientoCreadoId",
                table: "CambiosEstado");

            migrationBuilder.DropColumn(
                name: "SeguimientoCerradoId",
                table: "CambiosEstado");

            migrationBuilder.DropColumn(
                name: "SeguimientoCreadoId",
                table: "CambiosEstado");
        }
    }
}
