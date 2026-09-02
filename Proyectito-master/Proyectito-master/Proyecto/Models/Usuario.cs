using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Proyecto.Models;

[Table("usuarios")]
public class Usuario : BaseModel
{
    [PrimaryKey("id", false)]
    public int? Id { get; set; }

    [Column("correo")]
    public string Correo { get; set; } = "";

    [Column("contrasena")]
    public string Contrasena { get; set; } = "";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }
}