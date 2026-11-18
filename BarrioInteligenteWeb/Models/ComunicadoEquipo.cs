using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BarrioInteligenteWeb.Models
{
    public class ComunicadoEquipo
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "El asunto es obligatorio.")]
        [MaxLength(140)]
        public string Asunto { get; set; } = default!;

        [Required(ErrorMessage = "El mensaje es obligatorio.")]
        [MaxLength(1500)]
        public string Mensaje { get; set; } = default!;

        [Required]
        [MaxLength(30)]
        public string Prioridad { get; set; } = "Normal"; // Normal, Alta, Urgente

        [Required]
        [MaxLength(30)]
        public string Estado { get; set; } = "Enviado"; // Enviado, En Atención, Completado

        [MaxLength(1000)]
        public string? NotaRespuesta { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public DateTime? FechaActualizacion { get; set; }

        public int EntidadResponsableId { get; set; }

        [ForeignKey("EntidadResponsableId")]
        public EntidadResponsable? EntidadResponsable { get; set; }

        public int EmisorUsuarioId { get; set; }

        [ForeignKey("EmisorUsuarioId")]
        public Usuario? EmisorUsuario { get; set; }

        public int? AsignadoUsuarioId { get; set; }

        [ForeignKey("AsignadoUsuarioId")]
        public Usuario? AsignadoUsuario { get; set; }

        public int? ReporteId { get; set; }

        [ForeignKey("ReporteId")]
        public Reporte? Reporte { get; set; }
    }

    public class EquiposIndexViewModel
    {
        public List<EntidadResponsable> Entidades { get; set; } = new();
        public List<ComunicadoEquipo> Comunicados { get; set; } = new();
        public List<Usuario> Staff { get; set; } = new();
        public List<Reporte> ReportesActivos { get; set; } = new();
    }
}
