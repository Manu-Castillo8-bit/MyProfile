using System.Text.Json;
using System.Text.Json.Serialization;
using Supabase.Postgrest.Responses;

namespace Proyecto.Services;

// ── DTOs de lectura para el panel de administración (sin tocar los modelos de PostgREST) ──

public class UsuarioAdmin
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = "";
    [JsonPropertyName("correo")] public string Correo { get; set; } = "";
    [JsonPropertyName("rol")] public string Rol { get; set; } = "";

    public bool EsAdmin => string.Equals(Rol, "admin", StringComparison.OrdinalIgnoreCase);

    public string Iniciales
    {
        get
        {
            var n = (Nombre ?? "").Trim();
            if (n.Length == 0) return "?";
            var partes = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var iniciales = partes.Length >= 2
                ? partes[0][..1] + partes[^1][..1]
                : n[..1];
            return iniciales.ToUpperInvariant();
        }
    }

    public Color BadgeColor => EsAdmin ? Color.FromArgb("#B8860B") : Color.FromArgb("#1E3A5F");

    public string DescripcionRol => EsAdmin ? "Administrador" : "Usuario";
}

public class AdminTarea
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("titulo")] public string Titulo { get; set; } = "";
    [JsonPropertyName("descripcion")] public string Descripcion { get; set; } = "";
    [JsonPropertyName("fecha_vencimiento")] public DateTime? FechaVencimiento { get; set; }
    [JsonPropertyName("estado")] public string Estado { get; set; } = "";
}

public class AdminMovimiento
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("monto")] public decimal Monto { get; set; }
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("descripcion")] public string Descripcion { get; set; } = "";
    [JsonPropertyName("fecha")] public DateTime Fecha { get; set; }
}

public class AdminContrasena
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("sitio")] public string Sitio { get; set; } = "";
    [JsonPropertyName("usuario")] public string UsuarioCuenta { get; set; } = "";
    [JsonPropertyName("clave_cifrada")] public string ClaveCifrada { get; set; } = "";
}

public class AdminRecordatorio
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("frecuencia_minutos")] public int FrecuenciaMinutos { get; set; }
    [JsonPropertyName("activo")] public bool Activo { get; set; }
}

public class AdminDatosUsuario
{
    [JsonPropertyName("usuario")] public UsuarioAdmin Usuario { get; set; } = new();
    [JsonPropertyName("tareas")] public List<AdminTarea> Tareas { get; set; } = new();
    [JsonPropertyName("movimientos")] public List<AdminMovimiento> Movimientos { get; set; } = new();
    [JsonPropertyName("contrasenas")] public List<AdminContrasena> Contrasenas { get; set; } = new();
    [JsonPropertyName("recordatorios")] public List<AdminRecordatorio> Recordatorios { get; set; } = new();
}

// ── Servicio que invoca las funciones RPC de Supabase (solo en línea) ──

public static class AdminService
{
    public static bool EsAdmin =>
        string.Equals(SupabaseService.UsuarioActual?.Rol, "admin", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<string> ObtenerContenidoAsync(BaseResponse resp)
    {
        if (!string.IsNullOrWhiteSpace(resp.Content))
            return resp.Content;

        if (resp.ResponseMessage?.Content is { } contenido)
            return await contenido.ReadAsStringAsync();

        return "";
    }

    private static Dictionary<string, object> Parametros(params (string nombre, object valor)[] pares)
        => pares.ToDictionary(p => p.nombre, p => p.valor);

    private static async Task VerificarAsync()
    {
        if (!SyncService.Conectado)
            throw new InvalidOperationException("Necesitas conexión a internet para usar el panel de administrador.");
        if (!EsAdmin)
            throw new InvalidOperationException("No tienes permisos de administrador.");
    }

    // Llama a un RPC y traduce el error típico de autorización a un mensaje claro.
    private static async Task<T> RpcAsync<T>(string funcion, Dictionary<string, object> parametros, Func<T, bool> valido)
        where T : class
    {
        await VerificarAsync();

        try
        {
            var client = await SupabaseService.GetClientAsync();
            var resp = await client.Rpc(funcion, parametros);
            var json = await ObtenerContenidoAsync(resp);
            var resultado = Deserializar<T>(json);
            if (valido(resultado))
                return resultado!;

            throw new InvalidOperationException("La respuesta del servidor no fue válida.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TraducirError(ex);
            throw;
        }
    }

    private static async Task RpcSinResultadoAsync(string funcion, Dictionary<string, object> parametros)
    {
        await VerificarAsync();

        try
        {
            var client = await SupabaseService.GetClientAsync();
            await client.Rpc(funcion, parametros);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TraducirError(ex);
            throw;
        }
    }

    private static void TraducirError(Exception ex)
    {
        if (ex.Message.Contains("No autorizado", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("P0001", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "El servidor rechazó la sesión de administrador. " +
                "Verifica en Supabase que tu correo tenga rol 'admin' y que el enlace con tu cuenta de Auth esté correcto. " +
                "Después cierra sesión y vuelve a iniciar sesión en la app.", ex);
        }
    }

    public static async Task<List<UsuarioAdmin>> ListarUsuariosAsync()
    {
        var respuesta = await RpcAsync<List<UsuarioAdmin>>(
            "admin_listar_usuarios",
            new Dictionary<string, object>(),
            r => r is not null);

        return respuesta ?? new List<UsuarioAdmin>();
    }

    public static async Task<AdminDatosUsuario> ObtenerDatosUsuarioAsync(int idUsuario)
    {
        var respuesta = await RpcAsync<AdminDatosUsuario>(
            "admin_datos_usuario",
            Parametros(("p_usuario", idUsuario)),
            r => r is not null);

        return respuesta ?? new AdminDatosUsuario();
    }

    public static async Task EliminarTareaAsync(int idUsuario, int id)
        => await EjecutarAsync("admin_eliminar_tarea", idUsuario, id);

    public static async Task EliminarMovimientoAsync(int idUsuario, int id)
        => await EjecutarAsync("admin_eliminar_movimiento", idUsuario, id);

    public static async Task EliminarContrasenaAsync(int idUsuario, int id)
        => await EjecutarAsync("admin_eliminar_contrasena", idUsuario, id);

    public static async Task EliminarRecordatorioAsync(int idUsuario, int id)
        => await EjecutarAsync("admin_eliminar_recordatorio", idUsuario, id);

    public static async Task EliminarUsuarioAsync(int idUsuario)
        => await EjecutarAsync("admin_eliminar_usuario", idUsuario, -1);

    private static async Task EjecutarAsync(string funcion, int idUsuario, int id)
    {
        var parametros = id < 0
            ? Parametros(("p_usuario", idUsuario))
            : Parametros(("p_usuario", idUsuario), ("p_id", (long)id));

        await RpcSinResultadoAsync(funcion, parametros);
    }

    // ── CRUD: edición del perfil del usuario ──

    public static Task ActualizarUsuarioAsync(int idUsuario, string? nombre, string? rol)
    {
        var parametros = new Dictionary<string, object> { ["p_usuario"] = idUsuario };
        if (!string.IsNullOrWhiteSpace(nombre)) parametros["p_nombre"] = nombre;
        if (!string.IsNullOrWhiteSpace(rol)) parametros["p_rol"] = rol;
        return RpcSinResultadoAsync("admin_actualizar_usuario", parametros);
    }

    // ── CRUD: tareas ──

    public static Task InsertarTareaAsync(int idUsuario, string titulo, string descripcion, DateTime? fecha, string estado)
        => RpcSinResultadoAsync("admin_insertar_tarea", new Dictionary<string, object>
        {
            ["p_usuario"] = idUsuario,
            ["p_titulo"] = titulo,
            ["p_descripcion"] = descripcion ?? "",
            ["p_fecha_vencimiento"] = (object?)fecha ?? DBNull.Value,
            ["p_estado"] = estado
        });

    public static Task ActualizarTareaAsync(int idUsuario, int id, string? titulo, string? descripcion, DateTime? fecha, string? estado)
    {
        var parametros = new Dictionary<string, object> { ["p_usuario"] = idUsuario, ["p_tarea"] = (long)id };
        if (!string.IsNullOrWhiteSpace(titulo)) parametros["p_titulo"] = titulo;
        if (descripcion is not null) parametros["p_descripcion"] = descripcion;
        if (fecha is not null) parametros["p_fecha_vencimiento"] = fecha.Value;
        if (!string.IsNullOrWhiteSpace(estado)) parametros["p_estado"] = estado;
        return RpcSinResultadoAsync("admin_actualizar_tarea", parametros);
    }

    // ── CRUD: movimientos (ahorro) ──

    public static Task InsertarMovimientoAsync(int idUsuario, decimal monto, string tipo, string descripcion, DateTime? fecha)
        => RpcSinResultadoAsync("admin_insertar_movimiento", new Dictionary<string, object>
        {
            ["p_usuario"] = idUsuario,
            ["p_monto"] = monto,
            ["p_tipo"] = tipo,
            ["p_descripcion"] = descripcion ?? "",
            ["p_fecha"] = (object?)fecha ?? DBNull.Value
        });

    public static Task ActualizarMovimientoAsync(int idUsuario, int id, decimal? monto, string? tipo, string? descripcion, DateTime? fecha)
    {
        var parametros = new Dictionary<string, object> { ["p_usuario"] = idUsuario, ["p_movimiento"] = (long)id };
        if (monto is not null) parametros["p_monto"] = monto.Value;
        if (!string.IsNullOrWhiteSpace(tipo)) parametros["p_tipo"] = tipo;
        if (descripcion is not null) parametros["p_descripcion"] = descripcion;
        if (fecha is not null) parametros["p_fecha"] = fecha.Value;
        return RpcSinResultadoAsync("admin_actualizar_movimiento", parametros);
    }

    // ── CRUD: contraseñas ──

    public static Task InsertarContrasenaAsync(int idUsuario, string sitio, string usuarioCuenta, string clave)
        => RpcSinResultadoAsync("admin_insertar_contrasena", new Dictionary<string, object>
        {
            ["p_usuario"] = idUsuario,
            ["p_sitio"] = sitio,
            ["p_usuario_cuenta"] = usuarioCuenta ?? "",
            ["p_clave"] = clave ?? ""
        });

    public static Task ActualizarContrasenaAsync(int idUsuario, int id, string? sitio, string? usuarioCuenta, string? clave)
    {
        var parametros = new Dictionary<string, object> { ["p_usuario"] = idUsuario, ["p_contrasena"] = (long)id };
        if (!string.IsNullOrWhiteSpace(sitio)) parametros["p_sitio"] = sitio;
        if (usuarioCuenta is not null) parametros["p_usuario_cuenta"] = usuarioCuenta;
        if (clave is not null) parametros["p_clave"] = clave;
        return RpcSinResultadoAsync("admin_actualizar_contrasena", parametros);
    }

    // ── CRUD: recordatorios (salud) ──

    public static Task InsertarRecordatorioAsync(int idUsuario, string tipo, int frecuencia, bool activo)
        => RpcSinResultadoAsync("admin_insertar_recordatorio", new Dictionary<string, object>
        {
            ["p_usuario"] = idUsuario,
            ["p_tipo"] = tipo,
            ["p_frecuencia_minutos"] = frecuencia,
            ["p_activo"] = activo
        });

    public static Task ActualizarRecordatorioAsync(int idUsuario, int id, string? tipo, int? frecuencia, bool? activo)
    {
        var parametros = new Dictionary<string, object> { ["p_usuario"] = idUsuario, ["p_recordatorio"] = (long)id };
        if (!string.IsNullOrWhiteSpace(tipo)) parametros["p_tipo"] = tipo;
        if (frecuencia is not null) parametros["p_frecuencia_minutos"] = frecuencia.Value;
        if (activo is not null) parametros["p_activo"] = activo.Value;
        return RpcSinResultadoAsync("admin_actualizar_recordatorio", parametros);
    }

    private static T? Deserializar<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}