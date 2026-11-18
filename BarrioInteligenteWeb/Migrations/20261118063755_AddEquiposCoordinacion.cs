using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarrioInteligenteWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddEquiposCoordinacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF COL_LENGTH('Usuarios', 'FechaSuspensionHasta') IS NULL
                    ALTER TABLE [Usuarios] ADD [FechaSuspensionHasta] datetime2 NULL;
                IF COL_LENGTH('Usuarios', 'MotivoSuspension') IS NULL
                    ALTER TABLE [Usuarios] ADD [MotivoSuspension] nvarchar(max) NULL;
                IF COL_LENGTH('Usuarios', 'Rol') IS NULL
                    ALTER TABLE [Usuarios] ADD [Rol] nvarchar(max) NOT NULL CONSTRAINT [DF_Usuarios_Rol] DEFAULT N'Ciudadano';
                IF COL_LENGTH('Insignias', 'PuntosRequeridos') IS NULL
                    ALTER TABLE [Insignias] ADD [PuntosRequeridos] int NOT NULL CONSTRAINT [DF_Insignias_PuntosRequeridos] DEFAULT 0;
            ");

            migrationBuilder.CreateTable(
                name: "EntidadesResponsables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Siglas = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CategoriaServicio = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResponsableNombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorreoContacto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    TelefonoContacto = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CanalAtencion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ColorHex = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntidadesResponsables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComunicadosEquipos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Asunto = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Mensaje = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Prioridad = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NotaRespuesta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EntidadResponsableId = table.Column<int>(type: "int", nullable: false),
                    EmisorUsuarioId = table.Column<int>(type: "int", nullable: false),
                    AsignadoUsuarioId = table.Column<int>(type: "int", nullable: true),
                    ReporteId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComunicadosEquipos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComunicadosEquipos_EntidadesResponsables_EntidadResponsableId",
                        column: x => x.EntidadResponsableId,
                        principalTable: "EntidadesResponsables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComunicadosEquipos_Reportes_ReporteId",
                        column: x => x.ReporteId,
                        principalTable: "Reportes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComunicadosEquipos_Usuarios_AsignadoUsuarioId",
                        column: x => x.AsignadoUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComunicadosEquipos_Usuarios_EmisorUsuarioId",
                        column: x => x.EmisorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComunicadosEquipos_AsignadoUsuarioId",
                table: "ComunicadosEquipos",
                column: "AsignadoUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ComunicadosEquipos_EmisorUsuarioId",
                table: "ComunicadosEquipos",
                column: "EmisorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ComunicadosEquipos_EntidadResponsableId",
                table: "ComunicadosEquipos",
                column: "EntidadResponsableId");

            migrationBuilder.CreateIndex(
                name: "IX_ComunicadosEquipos_ReporteId",
                table: "ComunicadosEquipos",
                column: "ReporteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComunicadosEquipos");

            migrationBuilder.DropTable(
                name: "EntidadesResponsables");

            migrationBuilder.DropColumn(
                name: "FechaSuspensionHasta",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "MotivoSuspension",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Rol",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "PuntosRequeridos",
                table: "Insignias");
        }
    }
}
