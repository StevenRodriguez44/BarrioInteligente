using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using BarrioInteligenteWeb.Data;
using BarrioInteligenteWeb.Models;
using BarrioInteligenteWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarrioInteligenteWeb.Controllers
{
    [Authorize]
    public class EquiposController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<EquiposController> _logger;

        private static readonly string[] PrioridadesValidas = { "Normal", "Alta", "Urgente" };
        private static readonly string[] EstadosValidos = { "Enviado", "En Atención", "Completado" };

        public EquiposController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<EquiposController> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        private async Task<Usuario?> ObtenerUsuarioActualAsync()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idStr, out int userId)) return null;

            return await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        private static bool EsAdminUsuario(Usuario? u) =>
            u != null && (
                u.EsAdmin ||
                u.Rol == RolesUsuario.Administrador ||
                string.Equals(u.Correo, "stevenrodriguez77777@gmail.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Correo, "christopherxd2005@gmail.com", StringComparison.OrdinalIgnoreCase)
            );

        private static bool EsStaffUsuario(Usuario? u) =>
            u != null && (
                EsAdminUsuario(u) ||
                (!string.IsNullOrWhiteSpace(u.Rol) && u.Rol != RolesUsuario.Ciudadano)
            );

        private bool EsPeticionAjax() =>
            string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase) ||
            Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var usuario = await ObtenerUsuarioActualAsync();
            if (!EsStaffUsuario(usuario))
            {
                TempData["InfoMessage"] = "El apartado de Equipos está reservado para personal operativo y administradores.";
                return RedirectToAction("Index", "Reportes");
            }

            ViewBag.EsAdmin = EsAdminUsuario(usuario);
            ViewBag.EsStaff = true;
            ViewBag.FotoPerfil = usuario!.FotoPerfil;
            ViewBag.UsuarioActualId = usuario.Id;

            var model = new EquiposIndexViewModel
            {
                Entidades = await _context.EntidadesResponsables
                    .AsNoTracking()
                    .OrderByDescending(e => e.Activa)
                    .ThenBy(e => e.Id)
                    .ToListAsync(),

                Comunicados = await _context.ComunicadosEquipos
                    .AsNoTracking()
                    .Include(c => c.EntidadResponsable)
                    .Include(c => c.EmisorUsuario)
                    .Include(c => c.AsignadoUsuario)
                    .Include(c => c.Reporte)
                    .OrderByDescending(c => c.FechaCreacion)
                    .ToListAsync(),

                Staff = await _context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.EsAdmin ||
                                (u.Rol != null && u.Rol != RolesUsuario.Ciudadano) ||
                                u.Correo == "stevenrodriguez77777@gmail.com" ||
                                u.Correo == "christopherxd2005@gmail.com")
                    .OrderBy(u => u.NombreCompleto)
                    .ToListAsync(),

                ReportesActivos = await _context.Reportes
                    .AsNoTracking()
                    .Where(r => r.Estado == "Pendiente" || r.Estado == "En Proceso")
                    .OrderByDescending(r => r.Fecha)
                    .Take(100)
                    .ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarComunicado(
            int entidadResponsableId,
            string asunto,
            string mensaje,
            string prioridad = "Normal",
            int? asignadoUsuarioId = null,
            int? reporteId = null,
            bool marcarEnProceso = false,
            bool enviarCorreo = false)
        {
            var usuario = await ObtenerUsuarioActualAsync();
            if (!EsStaffUsuario(usuario))
            {
                return EsPeticionAjax()
                    ? Json(new { success = false, message = "Acceso denegado. Solo personal autorizado puede emitir comunicados." })
                    : RedirectToAction("Index", "Reportes");
            }

            if (string.IsNullOrWhiteSpace(asunto) || string.IsNullOrWhiteSpace(mensaje))
            {
                const string msg = "El asunto y el mensaje del comunicado son obligatorios.";
                if (EsPeticionAjax()) return Json(new { success = false, message = msg });
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(Index));
            }

            var entidad = await _context.EntidadesResponsables.FindAsync(entidadResponsableId);
            if (entidad == null)
            {
                const string msg = "La entidad o cuadrilla seleccionada no existe.";
                if (EsPeticionAjax()) return Json(new { success = false, message = msg });
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(Index));
            }

            var prioridadNormalizada = PrioridadesValidas.Contains(prioridad) ? prioridad : "Normal";
            var asuntoLimpio = asunto.Trim();
            if (asuntoLimpio.Length > 140) asuntoLimpio = asuntoLimpio[..140];
            var mensajeLimpio = mensaje.Trim();
            if (mensajeLimpio.Length > 1500) mensajeLimpio = mensajeLimpio[..1500];

            Usuario? asignado = null;
            if (asignadoUsuarioId.HasValue)
            {
                asignado = await _context.Usuarios
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == asignadoUsuarioId.Value);
                if (asignado == null) asignadoUsuarioId = null;
            }

            Reporte? reporteVinculado = null;
            bool reporteActualizado = false;
            if (reporteId.HasValue)
            {
                reporteVinculado = await _context.Reportes
                    .FirstOrDefaultAsync(r => r.Id == reporteId.Value);

                if (reporteVinculado == null)
                {
                    reporteId = null;
                }
                else if (marcarEnProceso && reporteVinculado.Estado != "En Proceso" && reporteVinculado.Estado != "Resuelto")
                {
                    var estadoAnterior = reporteVinculado.Estado;
                    reporteVinculado.Estado = "En Proceso";
                    _context.HistorialEstados.Add(new HistorialEstado
                    {
                        ReporteId = reporteVinculado.Id,
                        UsuarioId = usuario!.Id,
                        EstadoAnterior = estadoAnterior,
                        EstadoNuevo = "En Proceso",
                        FechaCambio = DateTime.Now
                    });
                    reporteActualizado = true;
                }
            }

            var comunicado = new ComunicadoEquipo
            {
                EntidadResponsableId = entidad.Id,
                EmisorUsuarioId = usuario!.Id,
                AsignadoUsuarioId = asignadoUsuarioId,
                ReporteId = reporteId,
                Asunto = asuntoLimpio,
                Mensaje = mensajeLimpio,
                Prioridad = prioridadNormalizada,
                Estado = "Enviado",
                FechaCreacion = DateTime.Now
            };

            _context.ComunicadosEquipos.Add(comunicado);
            await _context.SaveChangesAsync();

            bool correoEnviado = false;
            string? correoNota = null;
            if (enviarCorreo && !string.IsNullOrWhiteSpace(entidad.CorreoContacto))
            {
                try
                {
                    var reporteHtml = reporteVinculado != null
                        ? $"<p style='margin:8px 0;color:#374151;'><strong>Reporte vinculado:</strong> #{reporteVinculado.Id} — {WebUtility.HtmlEncode(reporteVinculado.Titulo)} ({WebUtility.HtmlEncode(reporteVinculado.DireccionFisica ?? reporteVinculado.Categoria)})</p>"
                        : string.Empty;

                    var cuerpoHtml = $@"
                        <div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;border:1px solid #e5e7eb;border-radius:12px;background:#ffffff;color:#111827;'>
                            <div style='border-bottom:2px solid #8B1E1E;padding-bottom:12px;margin-bottom:16px;'>
                                <span style='font-size:12px;font-weight:700;color:#8B1E1E;text-transform:uppercase;'>Barrio Inteligente • Despacho Operativo</span>
                                <h2 style='margin:6px 0 0 0;font-size:18px;color:#111827;'>{WebUtility.HtmlEncode(asuntoLimpio)}</h2>
                            </div>
                            <p style='margin:0 0 8px 0;font-size:13px;color:#4b5563;'><strong>Entidad:</strong> {WebUtility.HtmlEncode(entidad.Nombre)}</p>
                            <p style='margin:0 0 8px 0;font-size:13px;color:#4b5563;'><strong>Prioridad:</strong> {WebUtility.HtmlEncode(prioridadNormalizada)} &bull; <strong>Emitido por:</strong> {WebUtility.HtmlEncode(usuario.NombreCompleto)}</p>
                            {reporteHtml}
                            <div style='margin-top:16px;padding:14px;background:#f8f9fa;border:1px solid #e5e7eb;border-radius:8px;white-space:pre-line;font-size:14px;color:#1f2937;'>{WebUtility.HtmlEncode(mensajeLimpio)}</div>
                        </div>";

                    await _emailService.EnviarAsync(
                        entidad.CorreoContacto.Trim(),
                        $"[Barrio Inteligente • {prioridadNormalizada}] {asuntoLimpio}",
                        cuerpoHtml);

                    correoEnviado = true;
                    correoNota = $"Correo enviado a {entidad.CorreoContacto}.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo enviar notificación por correo a {Correo} para comunicado #{ComunicadoId}", entidad.CorreoContacto, comunicado.Id);
                    correoNota = "Comunicado guardado; no se pudo enviar el correo (SMTP no configurado o rechazado).";
                }
            }

            if (EsPeticionAjax())
            {
                return Json(new
                {
                    success = true,
                    message = reporteActualizado
                        ? "Comunicado despachado y reporte actualizado a En Proceso."
                        : "Comunicado despachado correctamente.",
                    correoEnviado,
                    correoNota,
                    comunicado = new
                    {
                        id = comunicado.Id,
                        asunto = comunicado.Asunto,
                        mensaje = comunicado.Mensaje,
                        prioridad = comunicado.Prioridad,
                        estado = comunicado.Estado,
                        notaRespuesta = comunicado.NotaRespuesta,
                        fechaCreacion = comunicado.FechaCreacion.ToString("dd/MM/yyyy HH:mm"),
                        entidadId = entidad.Id,
                        entidadNombre = entidad.Nombre,
                        entidadSiglas = string.IsNullOrWhiteSpace(entidad.Siglas) ? entidad.CategoriaServicio : entidad.Siglas,
                        entidadColor = entidad.ColorHex,
                        emisorNombre = usuario.NombreCompleto,
                        asignadoNombre = asignado?.NombreCompleto,
                        reporteId = reporteVinculado?.Id,
                        reporteTitulo = reporteVinculado?.Titulo,
                        reporteEstado = reporteVinculado?.Estado
                    }
                });
            }

            TempData["SuccessMessage"] = "Comunicado despachado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarEstadoComunicado(int id, string nuevoEstado, string? notaRespuesta)
        {
            var usuario = await ObtenerUsuarioActualAsync();
            if (!EsStaffUsuario(usuario))
            {
                return Json(new { success = false, message = "Acceso denegado." });
            }

            if (!EstadosValidos.Contains(nuevoEstado))
            {
                return Json(new { success = false, message = "Estado no válido." });
            }

            var comunicado = await _context.ComunicadosEquipos.FindAsync(id);
            if (comunicado == null)
            {
                return Json(new { success = false, message = "Comunicado no encontrado." });
            }

            comunicado.Estado = nuevoEstado;
            comunicado.FechaActualizacion = DateTime.Now;

            if (!string.IsNullOrWhiteSpace(notaRespuesta))
            {
                var notaLimpia = notaRespuesta.Trim();
                comunicado.NotaRespuesta = notaLimpia.Length > 1000 ? notaLimpia[..1000] : notaLimpia;
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Estado actualizado a {comunicado.Estado}.",
                id = comunicado.Id,
                estado = comunicado.Estado,
                notaRespuesta = comunicado.NotaRespuesta,
                fechaActualizacion = comunicado.FechaActualizacion?.ToString("dd/MM/yyyy HH:mm")
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearEntidad(
            string nombre,
            string? siglas,
            string categoriaServicio,
            string? responsableNombre,
            string? correoContacto,
            string? telefonoContacto,
            string? canalAtencion,
            string? colorHex)
        {
            var usuario = await ObtenerUsuarioActualAsync();
            if (!EsStaffUsuario(usuario))
            {
                return EsPeticionAjax()
                    ? Json(new { success = false, message = "Acceso denegado." })
                    : RedirectToAction("Index", "Reportes");
            }

            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(categoriaServicio))
            {
                const string msg = "El nombre y la categoría de servicio son obligatorios.";
                if (EsPeticionAjax()) return Json(new { success = false, message = msg });
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(Index));
            }

            var colorLimpio = string.IsNullOrWhiteSpace(colorHex) ? "#111827" : colorHex.Trim();
            if (!colorLimpio.StartsWith('#') || colorLimpio.Length > 20) colorLimpio = "#111827";

            static string? LimpiarOpcional(string? valor, int max)
            {
                if (string.IsNullOrWhiteSpace(valor)) return null;
                var trimmed = valor.Trim();
                return trimmed.Length > max ? trimmed[..max] : trimmed;
            }

            var nombreLimpio = nombre.Trim();
            if (nombreLimpio.Length > 120) nombreLimpio = nombreLimpio[..120];
            var catLimpia = categoriaServicio.Trim();
            if (catLimpia.Length > 50) catLimpia = catLimpia[..50];

            var entidad = new EntidadResponsable
            {
                Nombre = nombreLimpio,
                Siglas = LimpiarOpcional(siglas, 20)?.ToUpperInvariant(),
                CategoriaServicio = catLimpia,
                ResponsableNombre = LimpiarOpcional(responsableNombre, 100),
                CorreoContacto = LimpiarOpcional(correoContacto, 150),
                TelefonoContacto = LimpiarOpcional(telefonoContacto, 30),
                CanalAtencion = LimpiarOpcional(canalAtencion, 100),
                ColorHex = colorLimpio,
                Activa = true
            };

            _context.EntidadesResponsables.Add(entidad);
            await _context.SaveChangesAsync();

            if (EsPeticionAjax())
            {
                return Json(new
                {
                    success = true,
                    message = $"Entidad '{entidad.Nombre}' registrada correctamente.",
                    entidad = new
                    {
                        id = entidad.Id,
                        nombre = entidad.Nombre,
                        siglas = entidad.Siglas ?? entidad.CategoriaServicio,
                        categoriaServicio = entidad.CategoriaServicio,
                        responsableNombre = entidad.ResponsableNombre,
                        correoContacto = entidad.CorreoContacto,
                        telefonoContacto = entidad.TelefonoContacto,
                        canalAtencion = entidad.CanalAtencion,
                        colorHex = entidad.ColorHex
                    }
                });
            }

            TempData["SuccessMessage"] = $"Entidad '{entidad.Nombre}' registrada correctamente.";
            return RedirectToAction(nameof(Index));
        }
    }
}
