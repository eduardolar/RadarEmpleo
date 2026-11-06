using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadarEmpleo.Data.Migraciones
{
    /// <inheritdoc />
    public partial class FechaUltimoCambioConcurrencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sin cambios en el esquema: FechaUltimoCambio pasa a ser token de concurrencia, que solo
            // cambia el modelo (EF añade la condición a los UPDATE y DELETE). La migración existe para
            // que el snapshot coincida con el modelo y Migrate() no vea cambios pendientes.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
