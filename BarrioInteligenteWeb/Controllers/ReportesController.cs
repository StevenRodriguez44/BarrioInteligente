using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BarrioInteligenteWeb.Data;
using BarrioInteligenteWeb.Models;
using BarrioInteligenteWeb.Services;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using BarrioInteligenteWeb.Hubs;

namespace BarrioInteligenteWeb.Controllers
{
    [Authorize]
    public class ReportesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ReportesController> _logger;
        private readonly IHubContext<ReportesHub> _hubContext;
        private readonly IProfanityService _profanityService;
        private readonly IReputacionService _reputacionService;

        public ReportesController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            ILogger<ReportesController> logger,
            IHubContext<ReportesHub> hubContext,
            IProfanityService profanityService,
            IReputacionService reputacionService)
        {
            _context = context;
            _env = env;
            _logger = logger;
            _hubContext = hubContext;
            _profanityService = profanityService;
            _reputacionService = reputacionService;
        }

        private static readonly string[] _correosPropietarios =
        {
            "stevenrodriguez77777@gmail.com",
            "christopherxd2005@gmail.com"
        };

        private static readonly HashSet<string> _estadosValidos = new(StringComparer.Ordinal)
        {
            "Pendiente",
            "En Proceso",
            "Resuelto"
        };

        private int UsuarioActualId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private static bool EsCorreoPropietario(string? correo) =>
            !string.IsNullOrWhiteSpace(correo) &&
            _correosPropietarios.Any(c => string.Equals(c, correo, StringComparison.OrdinalIgnoreCase));

        private static bool EsUsuarioAdmin(Usuario? usuario) =>
            usuario != null &&
            (usuario.EsAdmin || usuario.Rol == RolesUsuario.Administrador || EsCorreoPropietario(usuario.Correo));

        private static bool EsUsuarioStaff(Usuario? usuario) =>
            usuario != null &&
            (EsUsuarioAdmin(usuario) ||
             (!string.IsNullOrWhiteSpace(usuario.Rol) && usuario.Rol != RolesUsuario.Ciudadano));

        private async Task<Usuario?> ObtenerUsuarioActualAsync()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId)) return null;
            return await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        }

        private async Task<bool> EsStaffAsync()
        {
            var usuario = await ObtenerUsuarioActualAsync();
            return EsUsuarioStaff(usuario);
        }

        private async Task CargarUsuarioViewBagAsync()
        {
            var usuario = await ObtenerUsuarioActualAsync();
            ViewBag.UsuarioId = usuario?.Id ?? UsuarioActualId;
            ViewBag.FotoPerfil = usuario?.FotoPerfil;
            ViewBag.EsAdmin = EsUsuarioAdmin(usuario);
            ViewBag.EsStaff = EsUsuarioStaff(usuario);
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                await CargarUsuarioViewBagAsync();
                var lista = await _context.Reportes
                    .AsNoTracking()
                    .OrderByDescending(r => r.Fecha)
                    .ToListAsync();
                return View(lista);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el índice de reportes");
                ViewBag.EsAdmin ??= false;
                ViewBag.EsStaff ??= false;
                return View(new List<Reporte>());
            }
        }

        public async Task<IActionResult> MisReportes()
        {
            try
            {
                await CargarUsuarioViewBagAsync();
                var lista = await _context.Reportes
                    .AsNoTracking()
                    .Where(r => r.UsuarioId == UsuarioActualId)
                    .OrderByDescending(r => r.Fecha)
                    .ToListAsync();
                return View("MisReportes", lista);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar Mis Reportes para usuario {UsuarioId}", UsuarioActualId);
                ViewBag.EsAdmin ??= false;
                ViewBag.EsStaff ??= false;
                return View("MisReportes", new List<Reporte>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                await CargarUsuarioViewBagAsync();

                var reportesCategorias = await _context.Reportes
                    .AsNoTracking()
                    .GroupBy(r => r.Categoria)
                    .Select(g => new { Categoria = g.Key, Count = g.Count() })
                    .ToListAsync();

                var estadosRaw = await _context.Reportes
                    .AsNoTracking()
                    .GroupBy(r => r.Estado)
                    .Select(g => new { Estado = g.Key, Count = g.Count() })
                    .ToListAsync();

                int countPendiente = estadosRaw
                    .Where(x => x.Estado == "Pendiente" || string.IsNullOrWhiteSpace(x.Estado))
                    .Sum(x => x.Count);
                int countEnProceso = estadosRaw
                    .Where(x => x.Estado == "En Proceso")
                    .Sum(x => x.Count);
                int countResuelto = estadosRaw
                    .Where(x => x.Estado == "Resuelto")
                    .Sum(x => x.Count);

                int totalReportes = countPendiente + countEnProceso + countResuelto;
                double tasaResolucion = totalReportes > 0
                    ? Math.Round(countResuelto * 100.0 / totalReportes, 1)
                    : 0.0;

                var reportesEstados = new[]
                {
                    new { Estado = "Pendiente", Count = countPendiente },
                    new { Estado = "En Proceso", Count = countEnProceso },
                    new { Estado = "Resuelto", Count = countResuelto }
                };

                var usuariosResumen = await _context.Usuarios
                    .AsNoTracking()
                    .Select(u => new { u.PuntosReputacion })
                    .ToListAsync();

                int repExcelente = 0, repBuena = 0, repRegular = 0, repMala = 0, repCritica = 0;
                foreach (var u in usuariosResumen)
                {
                    switch (ProfanityService.CalcularNivel(u.PuntosReputacion))
                    {
                        case NivelReputacion.Excelente: repExcelente++; break;
                        case NivelReputacion.Buena: repBuena++; break;
                        case NivelReputacion.Regular: repRegular++; break;
                        case NivelReputacion.Mala: repMala++; break;
                        case NivelReputacion.Critica: repCritica++; break;
                    }
                }

                var topUsuarios = await _context.Usuarios
                    .AsNoTracking()
                    .OrderByDescending(u => u.PuntosReputacion)
                    .ThenBy(u => u.Reputacion)
                    .Take(3)
                    .ToListAsync();

                var reportesRecientes = await _context.Reportes
                    .AsNoTracking()
                    .Include(r => r.Usuario)
                    .OrderBy(r => r.Estado == "Resuelto" ? 1 : 0)
                    .ThenByDescending(r => r.Fecha)
                    .Take(10)
                    .ToListAsync();

                ViewBag.TotalReportes = totalReportes;
                ViewBag.TotalPendientes = countPendiente;
                ViewBag.TotalEnProceso = countEnProceso;
                ViewBag.TotalResueltos = countResuelto;
                ViewBag.TasaResolucion = tasaResolucion;
                ViewBag.DataCategorias = JsonSerializer.Serialize(reportesCategorias);
                ViewBag.DataEstados = JsonSerializer.Serialize(reportesEstados);
                ViewBag.TopCiudadanos = topUsuarios;
                ViewBag.ReportesRecientes = reportesRecientes;
                ViewBag.TotalUsuarios = usuariosResumen.Count;
                ViewBag.RepExcelente = repExcelente;
                ViewBag.RepBuena = repBuena;
                ViewBag.RepRegular = repRegular;
                ViewBag.RepMala = repMala;
                ViewBag.RepCritica = repCritica;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el dashboard");
                ViewBag.EsAdmin ??= false;
                ViewBag.EsStaff ??= false;
                ViewBag.TotalReportes ??= 0;
                ViewBag.TotalPendientes ??= 0;
                ViewBag.TotalEnProceso ??= 0;
                ViewBag.TotalResueltos ??= 0;
                ViewBag.TasaResolucion ??= 0.0;
                ViewBag.DataCategorias ??= "[]";
                ViewBag.DataEstados ??= "[]";
                ViewBag.TopCiudadanos ??= new List<Usuario>();
                ViewBag.ReportesRecientes ??= new List<Reporte>();
                ViewBag.TotalUsuarios ??= 0;
                ViewBag.RepExcelente ??= 0;
                ViewBag.RepBuena ??= 0;
                ViewBag.RepRegular ??= 0;
                ViewBag.RepMala ??= 0;
                ViewBag.RepCritica ??= 0;
                return View();
            }
        }

        public async Task<IActionResult> Comunidad(string filtro = "recientes", double? lat = null, double? lon = null, string tipo = "Reporte")
        {
            try
            {
                await CargarUsuarioViewBagAsync();

                var query = (await _context.Reportes
                    .AsNoTracking()
                    .Include(r => r.Usuario)
                    .Include(r => r.Validaciones)
                    .Where(r => r.TipoPost == tipo || (tipo == "Reporte" && string.IsNullOrEmpty(r.TipoPost)))
                    .ToListAsync())
                    .AsEnumerable(); // Haversine

                List<Reporte> lista;

                if (filtro == "cercanos" && lat.HasValue && lon.HasValue)
                {
                    const double R = 6371; // km
                    double latR = lat.Value * Math.PI / 180;
                    double lonR = lon.Value * Math.PI / 180;

                    lista = query
                        .Where(r => r.Latitud != 0 || r.Longitud != 0)
                        .OrderBy(r =>
                        {
                            double dLat = (r.Latitud * Math.PI / 180) - latR;
                            double dLon = (r.Longitud * Math.PI / 180) - lonR;
                            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                                     + Math.Cos(latR) * Math.Cos(r.Latitud * Math.PI / 180)
                                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                            return 2 * R * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                        })
                        .ToList();
                }
                else
                {
                    lista = query
                        .OrderByDescending(r => r.Fecha)
                        .ToList();
                }

                ViewBag.FiltroActivo = filtro;
                ViewBag.TipoActual = tipo;

                return View(lista);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar Comunidad. Filtro={Filtro}", filtro);
                ViewBag.EsAdmin ??= false;
                ViewBag.EsStaff ??= false;
                return View(new List<Reporte>());
            }
        }

        public async Task<IActionResult> Crear()
        {
            var usuario = await _context.Usuarios.FindAsync(UsuarioActualId);
            if (usuario != null && usuario.EstaSuspendido)
            {
                var restante = usuario.FechaSuspensionHasta!.Value - DateTime.UtcNow;
                var tiempoTxt = restante.TotalDays >= 1 
                    ? $"{(int)restante.TotalDays} día(s)" 
                    : $"{restante.Hours} hora(s) y {restante.Minutes} min";
                TempData["ErrorMessage"] = $"⛔ Tu cuenta está suspendida temporalmente (restan {tiempoTxt}) por sanciones comunitarias. No puedes publicar reportes.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.FotoPerfil = usuario?.FotoPerfil;
            ViewBag.EsAdmin = EsUsuarioAdmin(usuario);
            ViewBag.EsStaff = EsUsuarioStaff(usuario);
            return View();
        }

        private static readonly (double lat, double lon)[] PolyTerritorioRD = new[]
        {
            (19.75, -71.74), (19.92, -71.65), (19.95, -71.00), (19.85, -70.65),
            (19.68, -69.95), (19.40, -69.80), (19.35, -69.25), (19.23, -69.10),
            (19.16, -69.25), (19.05, -69.45), (19.03, -68.90), (18.75, -68.40),
            (18.55, -68.30), (18.30, -68.40), (18.10, -68.65), (18.33, -68.85),
            (18.40, -69.00), (18.42, -69.35), (18.42, -69.70), (18.44, -70.00),
            (18.20, -70.25), (18.18, -70.55), (18.35, -70.65), (18.25, -71.05),
            (18.20, -71.08), (18.05, -71.10), (17.85, -71.25), (17.75, -71.40),
            (17.55, -71.52), (17.85, -71.70), (18.03, -71.75), (18.35, -71.72),
            (18.50, -71.86), (18.68, -71.75), (18.88, -71.71), (19.12, -71.71),
            (19.32, -71.70), (19.55, -71.72), (19.75, -71.74)
        };

        private static bool EsUbicacionValida(double lat, double lng)
        {
            if (lat < 17.50 || lat > 20.00 || lng < -71.86 || lng > -68.25)
                return false;

            bool inside = false;
            for (int i = 0, j = PolyTerritorioRD.Length - 1; i < PolyTerritorioRD.Length; j = i++)
            {
                var pi = PolyTerritorioRD[i];
                var pj = PolyTerritorioRD[j];
                bool intersect = ((pi.lon > lng) != (pj.lon > lng))
                    && (lat < (pj.lat - pi.lat) * (lng - pi.lon) / (pj.lon - pi.lon) + pi.lat);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(Reporte reporte, IFormFile? imagen)
        {
            try
            {
                var usuario = await _context.Usuarios.FindAsync(UsuarioActualId);
                if (usuario != null && usuario.EstaSuspendido)
                {
                    ModelState.AddModelError(string.Empty, "Tu cuenta está suspendida por sanciones de conducta y lenguaje.");
                    ViewBag.ErrorGuardado = $"Tu cuenta se encuentra suspendida temporalmente hasta el {usuario.FechaSuspensionHasta:dd/MM/yyyy HH:mm}.";
                    return View(reporte);
                }

                if (!EsUbicacionValida(reporte.Latitud, reporte.Longitud))
                {
                    ModelState.AddModelError(string.Empty, "La ubicación debe estar dentro del territorio terrestre de la República Dominicana (no en el mar ni en territorio extranjero).");
                    ViewBag.ErrorGuardado = "La ubicación seleccionada está en el mar o fuera de la República Dominicana.";
                    ViewBag.FotoPerfil = usuario?.FotoPerfil;
                    return View(reporte);
                }

                reporte.UsuarioId = UsuarioActualId;
                reporte.Fecha = DateTime.Now;

                var direccionParaTitulo = !string.IsNullOrEmpty(reporte.DireccionFisica) 
                    ? reporte.DireccionFisica 
                    : "zona reportada";
                reporte.Titulo = $"{reporte.Categoria} en {direccionParaTitulo}";

                if (imagen != null && imagen.Length > 0)
                {
                    var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                    Directory.CreateDirectory(uploadsDir);

                    var ext = Path.GetExtension(imagen.FileName).ToLowerInvariant();
                    if (!new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(ext))
                    {
                        _logger.LogWarning("Tipo de archivo no permitido: {Extension} por usuario {UsuarioId}", ext, UsuarioActualId);
                        ModelState.AddModelError("imagen", "Solo se permiten imágenes (.jpg, .png, .webp).");
                        return View(reporte);
                    }

                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadsDir, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                        await imagen.CopyToAsync(stream);

                    reporte.ImagenUrl = $"/uploads/{fileName}";
                    _logger.LogInformation("Imagen guardada: {Path}", reporte.ImagenUrl);
                }

                ModelState.Remove("Usuario");
                if (ModelState.IsValid)
                {
                    // ── Filtro de profanidades en título y descripción ANTES de guardar ──
                    if (!string.IsNullOrWhiteSpace(reporte.Titulo))
                    {
                        var filtroTitulo = await _profanityService.ValidarYCensurarAsync(
                            reporte.Titulo, UsuarioActualId);
                        reporte.Titulo = filtroTitulo.TextoCensurado;
                    }

                    if (!string.IsNullOrWhiteSpace(reporte.Descripcion))
                    {
                        var filtroDesc = await _profanityService.ValidarYCensurarAsync(
                            reporte.Descripcion, UsuarioActualId);
                        reporte.Descripcion = filtroDesc.TextoCensurado;
                    }

                    _context.Reportes.Add(reporte);
                    await _context.SaveChangesAsync();

                    // Recompensa cívica: +5 puntos por publicar reporte comunitario
                    await _reputacionService.AgregarPuntosAsync(UsuarioActualId, 5, "Reporte cívico publicado");

                    await _hubContext.Clients.All.SendAsync("RecibirNuevoReporte", reporte.Latitud, reporte.Longitud, reporte.Titulo, reporte.Categoria);

                    _logger.LogInformation("Reporte creado ID={Id} por usuario {UsuarioId}", reporte.Id, UsuarioActualId);
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico al crear reporte. UsuarioId={UsuarioId}, Titulo={Titulo}", UsuarioActualId, reporte.Titulo);
                ViewBag.ErrorGuardado = "Ocurrió un error al guardar el reporte. Intenta de nuevo.";
            }

            return View(reporte);
        }

        public async Task<IActionResult> Detalles(int id)
        {
            try
            {
                await CargarUsuarioViewBagAsync();
                var reporte = await _context.Reportes
                    .AsNoTracking()
                    .Include(r => r.Usuario)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (reporte == null)
                {
                    _logger.LogWarning("Reporte no encontrado: ID={Id}", id);
                    return NotFound();
                }

                var comentarios = await _context.Comentarios
                    .AsNoTracking()
                    .Include(c => c.Usuario)
                    .Include(c => c.Likes) // Estado Like
                    .Where(c => c.ReporteId == id)
                    .OrderBy(c => c.Fecha)
                    .ToListAsync();

                var yaVoto = await _context.Validaciones
                    .AsNoTracking()
                    .AnyAsync(v => v.ReporteId == id && v.UsuarioId == UsuarioActualId);

                return View(new ReporteDetalleViewModel
                {
                    Reporte = reporte,
                    Comentarios = comentarios,
                    YaVoto = yaVoto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar detalles del reporte ID={Id}", id);
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, string nuevoEstado)
        {
            try
            {
                if (!await EsStaffAsync())
                {
                    return Json(new { success = false, message = "Acceso denegado. Solo personal autorizado puede cambiar el estado." });
                }

                var estadoDestino = nuevoEstado?.Trim() ?? string.Empty;
                if (!_estadosValidos.Contains(estadoDestino))
                {
                    return Json(new { success = false, message = "Estado no válido. Usa Pendiente, En Proceso o Resuelto." });
                }

                var reporte = await _context.Reportes.FirstOrDefaultAsync(r => r.Id == id);
                if (reporte == null)
                {
                    return Json(new { success = false, message = "Reporte no encontrado." });
                }

                var estadoAnterior = string.IsNullOrWhiteSpace(reporte.Estado) ? "Pendiente" : reporte.Estado;
                if (string.Equals(estadoAnterior, estadoDestino, StringComparison.Ordinal))
                {
                    return Json(new
                    {
                        success = true,
                        estado = reporte.Estado,
                        estadoAnterior,
                        puntosOtorgados = 0,
                        message = $"El reporte ya se encuentra en estado {reporte.Estado}."
                    });
                }

                bool yaFueResuelto = estadoDestino == "Resuelto" &&
                    await _context.HistorialEstados.AnyAsync(h => h.ReporteId == id && h.EstadoNuevo == "Resuelto");

                reporte.Estado = estadoDestino;
                _context.HistorialEstados.Add(new HistorialEstado
                {
                    EstadoAnterior = estadoAnterior,
                    EstadoNuevo = estadoDestino,
                    FechaCambio = DateTime.Now,
                    ReporteId = id,
                    UsuarioId = UsuarioActualId
                });

                await _context.SaveChangesAsync();

                int puntosOtorgados = 0;
                if (estadoDestino == "Resuelto" && !yaFueResuelto)
                {
                    await _reputacionService.AgregarPuntosAsync(
                        reporte.UsuarioId,
                        10,
                        "Tu reporte comunitario fue atendido y resuelto");
                    puntosOtorgados = 10;
                }

                var mensaje = puntosOtorgados > 0
                    ? $"Reporte marcado como Resuelto (+{puntosOtorgados} pts al autor)."
                    : $"Estado actualizado de {estadoAnterior} a {estadoDestino}.";

                return Json(new
                {
                    success = true,
                    estado = reporte.Estado,
                    estadoAnterior,
                    puntosOtorgados,
                    message = mensaje
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar estado del reporte ID={Id} a {Estado}", id, nuevoEstado);
                return Json(new { success = false, message = "Error interno del servidor al cambiar el estado." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLikeComentario(int id)
        {
            try
            {
                var like = await _context.ComentariosLikes
                    .FirstOrDefaultAsync(l => l.ComentarioId == id && l.UsuarioId == UsuarioActualId);

                bool estaLikeado = false;

                if (like != null)
                {
                    // Quitar Like
                    _context.ComentariosLikes.Remove(like);
                }
                else
                {
                    // Dar Like
                    like = new ComentarioLike
                    {
                        ComentarioId = id,
                        UsuarioId = UsuarioActualId
                    };
                    _context.ComentariosLikes.Add(like);
                    estaLikeado = true;
                }

                await _context.SaveChangesAsync();

                var totalLikes = await _context.ComentariosLikes.CountAsync(l => l.ComentarioId == id);

                return Json(new { success = true, estaLikeado, totalLikes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar el like del comentario ID={Id} por el usuario {UsuarioId}", id, UsuarioActualId);
                return Json(new { success = false, message = "Error interno del servidor" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarReporte(int id)
        {
            try
            {
                var reporte = await _context.Reportes.FirstOrDefaultAsync(r => r.Id == id);
                if (reporte == null) return Json(new { success = false, message = "Reporte no encontrado." });

                var validacion = await _context.Validaciones
                    .FirstOrDefaultAsync(v => v.ReporteId == id && v.UsuarioId == UsuarioActualId);

                bool estaConfirmado = false;

                if (validacion != null)
                {
                    _context.Validaciones.Remove(validacion);
                    reporte.Upvotes = Math.Max(0, reporte.Upvotes - 1);
                }
                else
                {
                    _context.Validaciones.Add(new Validacion
                    {
                        ReporteId = id,
                        UsuarioId = UsuarioActualId,
                        FechaVoto = DateTime.Now
                    });
                    reporte.Upvotes++;
                    estaConfirmado = true;

                    // Si el voto viene de otro usuario, premiar al autor del reporte (+2 pts)
                    if (reporte.UsuarioId != UsuarioActualId)
                    {
                        await _reputacionService.AgregarPuntosAsync(reporte.UsuarioId, 2, "Validación ciudadana recibida en tu reporte");
                    }
                }

                await _context.SaveChangesAsync();
                
                return Json(new { success = true, estaConfirmado = estaConfirmado, nuevoConteo = reporte.Upvotes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al confirmar reporte. ReporteId={Id}, UsuarioId={UsuarioId}", id, UsuarioActualId);
                return Json(new { success = false, message = "Error interno del servidor." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarComentario(int reporteId, string texto)
        {
            try
            {
                var usuario = await _context.Usuarios.FindAsync(UsuarioActualId);
                if (usuario != null && usuario.EstaSuspendido)
                {
                    TempData["ErrorMessage"] = "⛔ Tu cuenta se encuentra temporalmente suspendida por sanciones comunitarias. No puedes publicar comentarios.";
                    return RedirectToAction(nameof(Detalles), new { id = reporteId });
                }

                if (!string.IsNullOrWhiteSpace(texto))
                {
                    // ── Filtro de profanidades ANTES de persistir ──
                    var filtro = await _profanityService.ValidarYCensurarAsync(
                        texto.Trim(), UsuarioActualId);
                    var textoFinal = filtro.TextoCensurado;

                    _context.Comentarios.Add(new Comentario
                    {
                        ReporteId = reporteId,
                        UsuarioId = UsuarioActualId,
                        Texto = textoFinal,
                        Fecha = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al agregar comentario. ReporteId={Id}, UsuarioId={UsuarioId}", reporteId, UsuarioActualId);
            }

            return RedirectToAction(nameof(Detalles), new { id = reporteId });
        }

        /// <summary>
        /// Endpoint JSON que retorna objetos enriquecidos de incidencias
        /// filtradas por categoría y estado, para el mapa de calor interactivo.
        /// Parámetros: ?categoria=Basura&amp;estadoFiltro=Activos
        /// estadoFiltro: "Activos" (Pendiente + En Proceso) | "Resueltos"
        /// Formato: [{ lat, lng, intensidad, id, titulo }, ...]
        /// </summary>
        [HttpGet]
        public IActionResult ObtenerCoordenadasCalor(string? categoria, string estadoFiltro = "Activos")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(categoria))
                    return Json(Array.Empty<object>());

                var query = _context.Reportes
                    .Where(r => r.Categoria == categoria && (r.Latitud != 0 || r.Longitud != 0));

                // Filtrar por estado
                if (estadoFiltro == "Activos")
                    query = query.Where(r => r.Estado == "Pendiente" || r.Estado == "En Proceso");
                else if (estadoFiltro == "Resueltos")
                    query = query.Where(r => r.Estado == "Resuelto");

                var datos = query
                    .Select(r => new
                    {
                        lat = r.Latitud,
                        lng = r.Longitud,
                        intensidad = 1.0, // Peso uniforme estilo Snap Map: la intensidad la define la densidad geográfica
                        id = r.Id,
                        titulo = r.Titulo,
                        direccion = r.DireccionFisica,
                        categoria = r.Categoria,
                        estado = r.Estado,
                        foto = string.IsNullOrWhiteSpace(r.ImagenUrl) ? "" : r.ImagenUrl
                    })
                    .ToList();

                return Json(datos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener coordenadas para el mapa de calor. Categoría={Categoria}, Estado={Estado}", categoria, estadoFiltro);
                return Json(Array.Empty<object>());
            }
        }
    }
}