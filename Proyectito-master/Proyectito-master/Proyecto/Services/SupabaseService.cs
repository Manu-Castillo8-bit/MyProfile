using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Proyecto.Services;

// 1. ESTRUCTURA DE LAS TABLAS (Modelos de Supabase)

[Table("usuario")]
public class Usuario : BaseModel
{
    [PrimaryKey("id_usuario", false)]
    [Column("id_usuario", ignoreOnInsert: true)]
    public int Id { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Column("correo")]
    public string Correo { get; set; } = string.Empty;

    [Column("auth_user_id")]
    public string? AuthUserId { get; set; }
}

[Table("movimiento_financiero")]
public class MovimientoFinanciero : BaseModel
{
    [PrimaryKey("id_movimiento", false)]
    [Column("id_movimiento", ignoreOnInsert: true)]
    public int IdMovimiento { get; set; }

    [Column("id_usuario")]
    public int IdUsuario { get; set; }

    [Column("monto")]
    public decimal Monto { get; set; }

    [Column("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [Column("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [Column("fecha")]
    public DateTime Fecha { get; set; }
}

[Table("contrasenas")]
public class Contrasena : BaseModel
{
    [PrimaryKey("id_contrasena", false)]
    [Column("id_contrasena", ignoreOnInsert: true)]
    public int IdContrasena { get; set; }

    [Column("id_usuario")]
    public int IdUsuario { get; set; }

    [Column("sitio_web")]
    public string SitioWeb { get; set; } = string.Empty;

    [Column("usuario_cuenta")]
    public string UsuarioCuenta { get; set; } = string.Empty;

    [Column("clave_cifrada")]
    public string ClaveCifrada { get; set; } = string.Empty;
}

[Table("tarea")]
public class Tarea : BaseModel
{
    [PrimaryKey("id_tarea", false)]
    [Column("id_tarea", ignoreOnInsert: true)]
    public int IdTarea { get; set; }

    [Column("id_usuario")]
    public int IdUsuario { get; set; }

    [Column("titulo")]
    public string Titulo { get; set; } = string.Empty;

    [Column("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [Column("fecha_vencimiento")]
    public DateTime? FechaVencimiento { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "pendiente";
}

[Table("recordatorio_salud")]
public class RecordatorioSalud : BaseModel
{
    [PrimaryKey("id_recordatorio", false)]
    [Column("id_recordatorio", ignoreOnInsert: true)]
    public int Id { get; set; }

    [Column("id_usuario")]
    public int IdUsuario { get; set; }

    [Column("tipo_recordatorio")]
    public string Tipo { get; set; } = string.Empty;

    [Column("frecuencia_minutos")]
    public int FrecuenciaMinutos { get; set; }

    [Column("frecuencia_valor")]
    public int FrecuenciaValor { get; set; }

    [Column("frecuencia_unidad")]
    public string FrecuenciaUnidad { get; set; } = "min";

    [Column("activo")]
    public bool Activo { get; set; }
}

// 2. CONFIGURACIÓN DE CONEXIÓN
public static class SupabaseConfig
{
    public const string Url = "https://mmvzkwklwibugzpyawmy.supabase.co";
    public const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1tdnprd2tsd2lidWd6cHlhd215Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODgxODE0NTgsImV4cCI6MjEwMzc1NzQ1OH0.41uyn16H27UbRaV1aKBGGPonFw_48Q1rdsHV2TKcQp8";
}

// 3. SEGURIDAD Y CIFRADO DE CONTRASEÑAS
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string contrasena)
    {
        using var derive = new Rfc2898DeriveBytes(contrasena, SaltSize, Iterations, HashAlgorithmName.SHA256);
        byte[] salt = derive.Salt;
        byte[] hash = derive.GetBytes(HashSize);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string contrasena, string almacenada)
    {
        var partes = almacenada.Split(':');
        if (partes.Length != 2)
            return false;

        byte[] salt;
        byte[] hash;
        try
        {
            salt = Convert.FromBase64String(partes[0]);
            hash = Convert.FromBase64String(partes[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var derive = new Rfc2898DeriveBytes(contrasena, salt, Iterations, HashAlgorithmName.SHA256);
        byte[] computed = derive.GetBytes(hash.Length);
        return CryptographicOperations.FixedTimeEquals(computed, hash);
    }
}

// 4. OPERACIONES DE NEGOCIO (offline-first)
public static class SupabaseService
{
    private static Client? _client;
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    // Sesión del usuario actual
    public static Usuario? UsuarioActual { get; private set; }

    private const string PrefId = "user_id";
    private const string PrefNombre = "user_nombre";
    private const string PrefCorreo = "user_correo";
    private const string PrefAuthUserId = "user_auth_user_id";

    public static void EstablecerSesion(Usuario usuario)
    {
        UsuarioActual = usuario;
        GuardarSesion();
        _ = RecordatorioScheduler.IniciarAsync();
    }

    public static void CerrarSesion()
    {
        UsuarioActual = null;
        RecordatorioScheduler.Detener();
        Preferences.Default.Remove(PrefId);
        Preferences.Default.Remove(PrefNombre);
        Preferences.Default.Remove(PrefCorreo);
        Preferences.Default.Remove(PrefAuthUserId);
    }

    private static void GuardarSesion()
    {
        if (UsuarioActual is null)
            return;

        Preferences.Default.Set(PrefId, UsuarioActual.Id);
        Preferences.Default.Set(PrefNombre, UsuarioActual.Nombre);
        Preferences.Default.Set(PrefCorreo, UsuarioActual.Correo);
        Preferences.Default.Set(PrefAuthUserId, UsuarioActual.AuthUserId ?? "");
    }

    public static void RestaurarSesion()
    {
        if (UsuarioActual is not null || !Preferences.Default.ContainsKey(PrefId))
            return;

        var authUserId = Preferences.Default.Get(PrefAuthUserId, "");
        UsuarioActual = new Usuario
        {
            Id = Preferences.Default.Get(PrefId, 0),
            Nombre = Preferences.Default.Get(PrefNombre, ""),
            Correo = Preferences.Default.Get(PrefCorreo, ""),
            AuthUserId = string.IsNullOrEmpty(authUserId) ? null : authUserId
        };
    }

    public static async Task<Client> GetClientAsync()
    {
        if (_client is not null)
            return _client;

        await InitLock.WaitAsync();
        try
        {
            _client ??= await CreateClientAsync();
        }
        finally
        {
            InitLock.Release();
        }

        return _client;
    }

    private static async Task<Client> CreateClientAsync()
    {
        var client = new Client(SupabaseConfig.Url, SupabaseConfig.AnonKey, new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = false
        });
        await client.InitializeAsync();
        return client;
    }

    // ── INICIO DE SESIÓN (online + offline) ──

    public static async Task<Usuario?> LoginAsync(string correo, string contrasena)
    {
        var correoNormalizado = NormalizarCorreo(correo);

        // 1) Intentar autenticación en Supabase (si hay conexión).
        if (SyncService.Conectado)
        {
            try
            {
                var client = await GetClientAsync();
                await client.Auth.SignIn(correoNormalizado, contrasena);

                var resultado = await client
                    .From<Usuario>()
                    .Where(u => u.Correo == correoNormalizado)
                    .Get();

                var usuario = resultado.Models.FirstOrDefault();
                if (usuario is null)
                {
                    var autenticado = client.Auth.CurrentUser;
                    string nombreRegistrado = "Usuario";
                    if (autenticado?.UserMetadata is { } meta &&
                        meta.TryGetValue("nombre", out var nombreMeta) &&
                        !string.IsNullOrWhiteSpace(nombreMeta?.ToString()))
                    {
                        nombreRegistrado = nombreMeta.ToString()!;
                    }

                    var porCrear = new Usuario
                    {
                        Nombre = nombreRegistrado,
                        Correo = correoNormalizado,
                        AuthUserId = autenticado?.Id
                    };

                    var insertado = await client.From<Usuario>().Insert(porCrear);
                    usuario = insertado.Models.FirstOrDefault();
                    if (usuario is null)
                        return null;
                }

                // Cacheamos el hash para permitir login offline la próxima vez.
                await LocalDatabase.GuardarCredencialesAsync(
                    correoNormalizado,
                    PasswordHasher.Hash(contrasena),
                    usuario.Nombre,
                    usuario.Id,
                    usuario.AuthUserId);

                EstablecerSesion(usuario);

                // Actualizamos los datos locales desde el servidor.
                await SyncService.SincronizarAsync();

                return usuario;
            }
            catch
            {
                // Si el servidor rechaza las credenciales, seguimos para no
                // ocultarle el error al usuario (ver comprobación final).
            }
        }

        // 2) Fallback OFFLINE: validar contra credenciales cacheadas.
        var cache = await LocalDatabase.ObtenerUsuarioPorCorreoAsync(correoNormalizado);
        if (cache is not null && PasswordHasher.Verify(contrasena, cache.HashContrasena))
        {
            var usuarioOffline = new Usuario
            {
                Id = cache.ServerId ?? 0,
                Nombre = cache.Nombre,
                Correo = cache.Correo,
                AuthUserId = cache.AuthUserId
            };

            if (usuarioOffline.Id <= 0)
            {
                // Usuario creado localmente antes de sincronizar: no tenemos
                // id de servidor, así que recurrimos a una sesión nominal.
                usuarioOffline.Id = ObtenerIdUsuarioNominal(correoNormalizado);
            }

            EstablecerSesion(usuarioOffline);
            return usuarioOffline;
        }

        // 3) Ni online (por error) ni offline: credenciales inválidas.
        if (SyncService.Conectado)
        {
            try
            {
                // Reconfirmamos online para diferenciar "sin red" de "credenciales incorrectas".
                var client = await GetClientAsync();
                await client.Auth.SignIn(correoNormalizado, contrasena);
            }
            catch
            {
                return null; // credenciales incorrectas en servidor
            }
        }

        return null;
    }

    // Id estable y positivo para usuarios que aún no tienen un id real de servidor.
    private static int ObtenerIdUsuarioNominal(string correo)
    {
        // Basado en el hash del correo: garantiza ser determinista y no colisionar
        // con ids reales pequeños en la práctica (suelen ser ≤ algunos miles).
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(correo));
        int valor = BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
        return 1_000_000 + (valor % 900_000_000);
    }

    public static async Task RegistrarAsync(string nombre, string correo, string contrasena)
    {
        var correoNormalizado = NormalizarCorreo(correo);

        if (!SyncService.Conectado)
            throw new InvalidOperationException("No hay conexión a internet. Conéctate para poder crear tu cuenta.");

        var cliente = await GetClientAsync();
        try
        {
            await cliente.Auth.SignUp(correoNormalizado, contrasena);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No se pudo crear la cuenta: {ex.Message}");
        }

        var usuarioAutenticado = cliente.Auth.CurrentUser;

        var existente = await cliente
            .From<Usuario>()
            .Where(u => u.Correo == correoNormalizado)
            .Get();

        var usuarioExistente = existente.Models.FirstOrDefault();
        if (usuarioExistente is not null)
        {
            if (string.IsNullOrEmpty(usuarioExistente.AuthUserId) && usuarioAutenticado?.Id is not null)
            {
                await cliente.From<Usuario>()
                    .Where(u => u.Id == usuarioExistente.Id)
                    .Set(u => u.AuthUserId, usuarioAutenticado.Id)
                    .Update();
            }

            await LocalDatabase.GuardarCredencialesAsync(
                correoNormalizado,
                PasswordHasher.Hash(contrasena),
                usuarioExistente.Nombre,
                usuarioExistente.Id,
                usuarioExistente.AuthUserId);

            EstablecerSesion(usuarioExistente);
            return;
        }

        var respuesta = await cliente.From<Usuario>().Insert(new Usuario
        {
            Nombre = nombre.Trim(),
            Correo = correoNormalizado,
            AuthUserId = usuarioAutenticado?.Id
        });

        var usuarioCreado = respuesta.Models.FirstOrDefault();
        if (usuarioCreado is not null)
        {
            await LocalDatabase.GuardarCredencialesAsync(
                correoNormalizado,
                PasswordHasher.Hash(contrasena),
                usuarioCreado.Nombre,
                usuarioCreado.Id,
                usuarioCreado.AuthUserId);

            EstablecerSesion(usuarioCreado);
        }
    }

    private static string NormalizarCorreo(string correo) => correo.Trim().ToLower();

    // ── MOVIMIENTOS FINANCIEROS ──

    public static async Task<decimal> ObtenerSaldoAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return 0;

        var movimientos = await ObtenerMovimientosAsync();
        decimal saldo = 0;
        foreach (var m in movimientos)
        {
            if (m.Tipo == "ingreso")
                saldo += m.Monto;
            else if (m.Tipo == "gasto")
                saldo -= m.Monto;
        }
        return saldo;
    }

    public static async Task<List<MovimientoFinanciero>> ObtenerMovimientosAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return new List<MovimientoFinanciero>();

        var locales = await LocalDatabase.ObtenerMovimientosAsync(usuario.Id);
        return locales
            .OrderByDescending(m => m.Fecha)
            .Select(m => new MovimientoFinanciero
            {
                IdMovimiento = LocalDatabase.IdInterfaz(m.ServerId, m.IdLocal),
                IdUsuario = usuario.Id,
                Monto = m.Monto,
                Tipo = m.Tipo,
                Descripcion = m.Descripcion,
                Fecha = m.Fecha
            })
            .ToList();
    }

    public static async Task RegistrarMovimientoAsync(decimal monto, string tipo, string descripcion)
    {
        var usuario = UsuarioActual;
        if (usuario is null || usuario.Id <= 0)
            throw new InvalidOperationException("No hay una sesión activa con ID de usuario válido.");

        await LocalDatabase.InsertarMovimientoPendienteAsync(new MovimientoOffline
        {
            IdUsuario = usuario.Id,
            Monto = monto,
            Tipo = tipo,
            Descripcion = descripcion.Trim(),
            Fecha = DateTime.UtcNow
        });

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    // ── CONTRASEÑAS (CRUD) ──

    public static async Task<List<Contrasena>> ObtenerContrasenasAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return new List<Contrasena>();

        var locales = await LocalDatabase.ObtenerContrasenasAsync(usuario.Id);
        return locales
            .OrderByDescending(c => c.IdLocal)
            .Select(c => new Contrasena
            {
                IdContrasena = LocalDatabase.IdInterfaz(c.ServerId, c.IdLocal),
                IdUsuario = usuario.Id,
                SitioWeb = c.SitioWeb,
                UsuarioCuenta = c.UsuarioCuenta,
                ClaveCifrada = c.ClaveCifrada
            })
            .ToList();
    }

    public static async Task CrearContrasenaAsync(string sitioWeb, string usuarioCuenta, string clave)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            throw new InvalidOperationException("No hay sesión activa.");

        await LocalDatabase.InsertarContrasenaPendienteAsync(new ContrasenaOffline
        {
            IdUsuario = usuario.Id,
            SitioWeb = sitioWeb.Trim(),
            UsuarioCuenta = usuarioCuenta.Trim(),
            ClaveCifrada = PasswordHasher.Hash(clave)
        });

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    public static async Task ActualizarContrasenaAsync(int idContrasena, string sitioWeb, string usuarioCuenta, string clave)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            throw new InvalidOperationException("No hay sesión activa.");

        var fila = await LocalDatabase.ObtenerContrasenaPorInterfazAsync(usuario.Id, idContrasena);
        if (fila is null)
            return;

        fila.SitioWeb = sitioWeb.Trim();
        fila.UsuarioCuenta = usuarioCuenta.Trim();
        if (!string.IsNullOrWhiteSpace(clave))
            fila.ClaveCifrada = PasswordHasher.Hash(clave);

        await LocalDatabase.ActualizarContrasenaPendienteAsync(fila);

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    public static async Task EliminarContrasenaAsync(int idContrasena)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            return;

        var fila = await LocalDatabase.ObtenerContrasenaPorInterfazAsync(usuario.Id, idContrasena);
        if (fila is null)
            return;

        await LocalDatabase.MarcarContrasenaEliminadaAsync(fila);

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    // ── TAREAS (CRUD) ──

    public static async Task<List<Tarea>> ObtenerTareasAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return new List<Tarea>();

        var locales = await LocalDatabase.ObtenerTareasAsync(usuario.Id);
        return locales
            .OrderByDescending(t => t.IdLocal)
            .Select(t => new Tarea
            {
                IdTarea = LocalDatabase.IdInterfaz(t.ServerId, t.IdLocal),
                IdUsuario = usuario.Id,
                Titulo = t.Titulo,
                Descripcion = t.Descripcion,
                FechaVencimiento = t.FechaVencimiento,
                Estado = t.Estado
            })
            .ToList();
    }

    public static async Task CrearTareaAsync(string titulo, string descripcion, DateTime? fechaVencimiento, string estado)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            throw new InvalidOperationException("No hay sesión activa.");

        await LocalDatabase.InsertarTareaPendienteAsync(new TareaOffline
        {
            IdUsuario = usuario.Id,
            Titulo = titulo.Trim(),
            Descripcion = descripcion?.Trim() ?? "",
            FechaVencimiento = fechaVencimiento,
            Estado = string.IsNullOrWhiteSpace(estado) ? "pendiente" : estado.Trim()
        });

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    public static async Task ActualizarTareaAsync(int idTarea, string titulo, string descripcion, DateTime? fechaVencimiento, string estado)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            throw new InvalidOperationException("No hay sesión activa.");

        var fila = await LocalDatabase.ObtenerTareaPorInterfazAsync(usuario.Id, idTarea);
        if (fila is null)
            return;

        fila.Titulo = titulo.Trim();
        fila.Descripcion = descripcion?.Trim() ?? "";
        fila.FechaVencimiento = fechaVencimiento;
        fila.Estado = string.IsNullOrWhiteSpace(estado) ? "pendiente" : estado.Trim();

        await LocalDatabase.ActualizarTareaPendienteAsync(fila);

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    public static async Task CambiarEstadoTareaAsync(int idTarea, string estado)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            return;

        var fila = await LocalDatabase.ObtenerTareaPorInterfazAsync(usuario.Id, idTarea);
        if (fila is null)
            return;

        fila.Estado = estado.Trim().ToLower();
        await LocalDatabase.ActualizarTareaPendienteAsync(fila);

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    public static async Task EliminarTareaAsync(int idTarea)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            return;

        var fila = await LocalDatabase.ObtenerTareaPorInterfazAsync(usuario.Id, idTarea);
        if (fila is null)
            return;

        await LocalDatabase.MarcarTareaEliminadaAsync(fila);

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    // ── SALUD (PREFERENCIAS DE RECORDATORIOS) ──

    public static async Task<List<RecordatorioSalud>> ObtenerRecordatoriosAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return new List<RecordatorioSalud>();

        var locales = await LocalDatabase.ObtenerRecordatoriosAsync(usuario.Id);
        return locales
            .Select(r => new RecordatorioSalud
            {
                Id = LocalDatabase.IdInterfaz(r.ServerId, r.IdLocal),
                IdUsuario = usuario.Id,
                Tipo = r.Tipo,
                FrecuenciaMinutos = r.FrecuenciaMinutos,
                FrecuenciaValor = r.FrecuenciaValor,
                FrecuenciaUnidad = r.FrecuenciaUnidad,
                Activo = r.Activo
            })
            .ToList();
    }

    // Convierte (cantidad + unidad) a la duración en minutos que se guarda en la BD.
    public static int ResolverMinutos(int valor, string unidad)
    {
        var u = (unidad ?? "min").Trim().ToLowerInvariant();
        return u switch
        {
            "h" or "hora" or "horas" => valor * 60,
            "d" or "dia" or "dias" or "día" or "días" => valor * 60 * 24,
            _ => valor
        };
    }

    public static async Task GuardarRecordatorioAsync(string tipo, int frecuenciaValor, string frecuenciaUnidad, bool activo)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            throw new InvalidOperationException("No hay sesión activa.");

        var existente = await LocalDatabase.ObtenerRecordatorioPorTipoAsync(usuario.Id, tipo);
        int minutos = ResolverMinutos(frecuenciaValor, frecuenciaUnidad);

        await LocalDatabase.GuardarRecordatorioPendienteAsync(new RecordatorioSaludOffline
        {
            IdUsuario = usuario.Id,
            ServerId = existente?.ServerId,
            Tipo = tipo,
            FrecuenciaValor = frecuenciaValor,
            FrecuenciaUnidad = frecuenciaUnidad,
            FrecuenciaMinutos = minutos,
            Activo = activo
        });

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }

    public static async Task EliminarRecordatorioAsync(int idRecordatorio)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            return;

        var locales = await LocalDatabase.ObtenerRecordatoriosAsync(usuario.Id);
        var fila = locales.FirstOrDefault(r => LocalDatabase.IdInterfaz(r.ServerId, r.IdLocal) == idRecordatorio);
        if (fila is null)
            return;

        await LocalDatabase.MarcarRecordatorioEliminadoAsync(fila);

        if (SyncService.Conectado)
            await SyncService.SincronizarAsync();
    }
}