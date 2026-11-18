using System.ComponentModel.DataAnnotations;

namespace BarrioInteligenteWeb.Models
{
    public class EntidadResponsable
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre de la entidad es obligatorio.")]
        [MaxLength(120)]
        public string Nombre { get; set; } = default!;

        [MaxLength(20)]
        public string? Siglas { get; set; }

        [Required(ErrorMessage = "La categoría de servicio es obligatoria.")]
        [MaxLength(50)]
        public string CategoriaServicio { get; set; } = "General";

        [MaxLength(100)]
        public string? ResponsableNombre { get; set; }

        [MaxLength(150)]
        public string? CorreoContacto { get; set; }

        [MaxLength(30)]
        public string? TelefonoContacto { get; set; }

        [MaxLength(100)]
        public string? CanalAtencion { get; set; }

        [MaxLength(20)]
        public string ColorHex { get; set; } = "#111827";

        public bool Activa { get; set; } = true;

        public virtual ICollection<ComunicadoEquipo> Comunicados { get; set; } = new List<ComunicadoEquipo>();
    }
}
