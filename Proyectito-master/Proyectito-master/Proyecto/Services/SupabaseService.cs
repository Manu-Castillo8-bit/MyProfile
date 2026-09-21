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
    public string Estado { get; set; } = "Pendiente";
}

public static class EstadoTarea
{
    public const string Pendiente = "Pendiente";
    public const string Completado = "Completado";

    public static string Normalizar(string estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
            return Pendiente;
        if (estado.StartsWith("complet", StringComparison.OrdinalIgnoreCase))
            return Completado;
        if (estado.StartsWith("pend", StringComparison.OrdinalIgnoreCase))
            return Pendiente;
        return estado.Trim();
    }

    public static bool EsCompletado(string estado)
    {
        return string.Equals(Normalizar(estado), Completado, StringComparison.OrdinalIgnoreCase);
    }
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

    // Los ids nominales (sesión offline sin id de servidor) viven en un rango
    // alto para no chocar con los id_usuario reales de Supabase.
    private const int RangoInicioIdNominal = 1_000_000;

    // True cuando la sesión tiene un id_de_usuario real (no nominal), es decir,
    // hay a dónde subir los datos a Supabase.
    public static bool SesionConIdServidor =>
        UsuarioActual is { Id: > 0 } && !EsIdNominal(UsuarioActual.Id);

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

        // Una sesión restaurada sin id real sería inservible (todas las
        // operaciones la rechazarían): mejor descartarla y pedir login.
        int id = Preferences.Default.Get(PrefId, 0);
        if (id <= 0)
        {
            CerrarSesion();
            return;
        }

        var authUserId = Preferences.Default.Get(PrefAuthUserId, "");
        UsuarioActual = new Usuario
        {
            Id = id,
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

                var usuario = await ResolverUsuarioOnlineAsync(client, correoNormalizado);
                if (usuario is null)
                    return null;

                // Sana los datos que pudieron crearse en una sesión sin id de
                // servidor (modo offline): los mueve al id real para que se
                // sincronicen con Supabase en lugar de quedarse huérfanos.
                await RepararDatosLocalesAsync(correoNormalizado, usuario.Id);

                // Cacheamos el hash para permitir login offline la próxima vez.
                await LocalDatabase.GuardarCredencialesAsync(
                    correoNormalizado,
                    PasswordHasher.Hash(contrasena),
                    usuario.Nombre,
                    usuario.Id,
                    usuario.AuthUserId);

                EstablecerSesion(usuario);

                // Actualizamos los datos locales desde el servidor.
                try { await SyncService.SincronizarAsync(); } catch { }

                return usuario;
            }
            catch
            {
                // Si el servidor no respondió, se cae al modo offline.
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
                // No tenemos id de servidor, así que recurrimos a una sesión
                // nominal. Sus datos se reatribuirán al id real en el próximo
                // login online (ver RepararDatosLocalesAsync).
                usuarioOffline.Id = ObtenerIdUsuarioNominal(correoNormalizado);
            }

            EstablecerSesion(usuarioOffline);
            return usuarioOffline;
        }

        // 3) Ni online (por error) ni offline: credenciales inválidas.
        return null;
    }

    // Resuelve la fila real de la tabla "usuario" para el usuario ya autenticado
    // en Auth. Con esta lógica una cuenta SIEMPRE queda con un id_usuario real:
    //   a) Busca por auth_user_id (inmune a correos duplicados y a RLS por dueño).
    //   b) Si no, por correo.
    //   c) Si la fila no existe, la crea vinculada al usuario de Auth.
    // Nunca devuelve un usuario con id <= 0: mejor fallar que crear una sesión rota.
    private static async Task<Usuario?> ResolverUsuarioOnlineAsync(Client client, string correo)
    {
        var autenticado = client.Auth.CurrentUser;

        // a) La vía más fiable: el id de Auth ya está enlazado en la tabla.
        if (!string.IsNullOrEmpty(autenticado?.Id))
        {
            var porAuth = await client.From<Usuario>().Where(u => u.AuthUserId == autenticado.Id).Get();
            var encontrado = porAuth.Models.FirstOrDefault();
            if (encontrado is not null && encontrado.Id > 0)
                return encontrado;
        }

        // b) Búsqueda por correo.
        var porCorreo = await client.From<Usuario>().Where(u => u.Correo == correo).Get();
        var usuario = porCorreo.Models.FirstOrDefault();
        if (usuario is not null)
        {
            if (string.IsNullOrEmpty(usuario.AuthUserId) && !string.IsNullOrEmpty(autenticado?.Id))
            {
                // Fila huérfana: enlazamos el id de Auth para que el RLS por
                // dueño (auth.uid() = auth_user_id) permita verla/modificarla.
                try
                {
                    await client.From<Usuario>()
                        .Where(u => u.Id == usuario.Id)
                        .Set(u => u.AuthUserId, autenticado!.Id)
                        .Update();
                    usuario.AuthUserId = autenticado.Id;
                }
                catch { }
            }

            if (usuario.Id > 0)
                return usuario;

            // El modelo llegó sin id (mala señal): reintento por auth_user_id.
            if (!string.IsNullOrEmpty(autenticado?.Id))
            {
                var porAuth2 = await client.From<Usuario>().Where(u => u.AuthUserId == autenticado.Id).Get();
                var conId = porAuth2.Models.FirstOrDefault();
                if (conId is not null && conId.Id > 0)
                    return conId;
            }
            return null;
        }

        // c) No existe fila: se crea (requiere el usuario de Auth para enlazarlo).
        if (autenticado is null)
            return null;

        string nombreRegistrado = "Usuario";
        if (autenticado.UserMetadata is { } meta &&
            meta.TryGetValue("nombre", out var nombreMeta) &&
            !string.IsNullOrWhiteSpace(nombreMeta?.ToString()))
        {
            nombreRegistrado = nombreMeta.ToString()!;
        }

        try
        {
            var insertado = await client.From<Usuario>().Insert(new Usuario
            {
                Nombre = nombreRegistrado,
                Correo = correo,
                AuthUserId = autenticado.Id
            });
            var creado = insertado.Models.FirstOrDefault();
            if (creado is not null && creado.Id > 0)
                return creado;

            // La creación no devolvió la fila (RLS de escritura o preferencias
            // de PostgREST): la releemos por correo para recuperar el id real.
            var releido = await client.From<Usuario>().Where(u => u.Correo == correo).Get();
            var re = releido.Models.FirstOrDefault();
            if (re is not null && re.Id > 0)
                return re;
        }
        catch
        {
            // Posible correo duplicado con fila invisible por RLS: reintento lectura.
            var releido = await client.From<Usuario>().Where(u => u.Correo == correo).Get();
            var re = releido.Models.FirstOrDefault();
            if (re is not null && re.Id > 0)
                return re;
        }

        return null;
    }

    // Si antes se trabajó sin id de servidor (sesión nominal derivada del
    // correo), mueve sus datos locales al id real recién obtenido.
    private static async Task RepararDatosLocalesAsync(string correo, int idReal)
    {
        if (idReal <= 0)
            return;

        int idNominalAntiguo = ObtenerIdUsuarioNominal(correo);
        await LocalDatabase.ReatribuirDuennoAsync(idNominalAntiguo, idReal);

        // Datos de sesiones rotas antiguas (id_usuario = 0) también pasan al
        // dueño real. Asume un solo usuario por dispositivo.
        await LocalDatabase.ReatribuirDuennoAsync(0, idReal);
    }

    // Id estable y positivo para usuarios que aún no tienen un id real de servidor.
    private static int ObtenerIdUsuarioNominal(string correo)
    {
        // Basado en el hash del correo: garantiza ser determinista y no colisionar
        // con ids reales pequeños en la práctica (suelen ser ≤ algunos miles).
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(correo));
        int valor = BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
        return RangoInicioIdNominal + (valor % 900_000_000);
    }

    private static bool EsIdNominal(int id) => id >= RangoInicioIdNominal;

    public static async Task RegistrarAsync(string nombre, string correo, string contrasena)
    {
        var correoNormalizado = NormalizarCorreo(correo);

        if (!SyncService.Conectado)
            throw new InvalidOperationException("No hay conexión a internet. Conéctate para poder crear tu cuenta.");

        var cliente = await GetClientAsync();
        try
        {
            await cliente.Auth.SignUp(correoNormalizado, contrasena, new Supabase.Gotrue.SignUpOptions
            {
                Data = new Dictionary<string, object> { ["nombre"] = nombre }
            });
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Ese correo ya está registrado. Prueba con otro o usa tu cuenta existente.");
            throw new InvalidOperationException($"No se pudo crear la cuenta: {ex.Message}");
        }

        // La versión de gotrue usada no siempre fija la sesión tras el alta
        // (por eso a veces fallaba con "No se pudo confirmar tu cuenta").
        // Si no hay usuario autenticado, hacemos login explícito con las
        // credenciales recién creadas para resolver la fila de la tabla usuario.
        if (cliente.Auth.CurrentUser is null)
        {
            try
            {
                await cliente.Auth.SignIn(correoNormalizado, contrasena);
            }
            catch (Exception ex)
            {
                var detalle = ex.Message.Contains("not confirmed", StringComparison.OrdinalIgnoreCase)
                    ? "El correo requiere confirmación. En Supabase desactiva 'Confirm email' (Authentication → Sign In / Providers → Email) y en 'Authentication → Users' borra este usuario creado antes."
                    : ex.Message;
                throw new InvalidOperationException($"La cuenta se creó pero no se pudo iniciar sesión automáticamente. {detalle}");
            }
        }

        var usuario = await ResolverUsuarioOnlineAsync(cliente, correoNormalizado);
        if (usuario is null)
            throw new InvalidOperationException(
                "No se pudo iniciar sesión automáticamente tras crear la cuenta. Revisa la conexión e inténtalo de nuevo o ve a Iniciar sesión con tu correo y contraseña.");

        // La reparación/caché local son mejorables: un fallo aquí (p. ej. una
        // base local antigua) NO debe impedir que la cuenta recién creada entre.
        try { await RepararDatosLocalesAsync(correoNormalizado, usuario.Id); } catch { }
        try { await LocalDatabase.GuardarCredencialesAsync(
            correoNormalizado,
            PasswordHasher.Hash(contrasena),
            usuario.Nombre,
            usuario.Id,
            usuario.AuthUserId); } catch { }

        EstablecerSesion(usuario);

        try { await SyncService.SincronizarAsync(); } catch { }
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

    public static async Task<List<MovimientoFinanciero>> ObtenerHistorialAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return new List<MovimientoFinanciero>();

        var locales = await LocalDatabase.ObtenerHistorialAsync(usuario.Id);
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

    public static async Task EliminarMovimientoAsync(int idMovimiento)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            return;

        var fila = await LocalDatabase.ObtenerMovimientoPorInterfazAsync(usuario.Id, idMovimiento);
        if (fila is null)
            return;

        // Solo se oculta del historial; el saldo total NO cambia.
        await LocalDatabase.OcultarMovimientoAsync(fila);
    }

    public static async Task ActualizarMovimientoAsync(int idMovimiento, decimal monto, string tipo, string descripcion)
    {
        var usuario = UsuarioActual;
        if (usuario is null || usuario.Id <= 0)
            throw new InvalidOperationException("No hay una sesión activa con ID de usuario válido.");

        var fila = await LocalDatabase.ObtenerMovimientoPorInterfazAsync(usuario.Id, idMovimiento);
        if (fila is null)
            throw new InvalidOperationException("El movimiento ya no existe.");

        if (fila.Oculto)
            throw new InvalidOperationException("No se puede modificar un movimiento oculto.");

        fila.Monto = monto;
        fila.Tipo = tipo;
        fila.Descripcion = descripcion.Trim();

        await LocalDatabase.ActualizarMovimientoPendienteAsync(fila);

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
            Estado = EstadoTarea.Normalizar(estado)
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
        fila.Estado = EstadoTarea.Normalizar(estado);

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

        fila.Estado = EstadoTarea.Normalizar(estado);
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

        // Solo debe existir una fila por tipo (agua, descanso, tareas). Si por
        // alguna razón hay duplicados locales, se devuelve el canónico: el que
        // tiene ServerId (dato sincronizado) o, en su defecto, el más reciente.
        return locales
            .GroupBy(r => r.Tipo, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(r => r.ServerId is not null)
                .ThenByDescending(r => r.Modificado)
                .First())
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

        // Si la fila encontrada no tiene ServerId pero existe otra del mismo tipo
        // que sí lo tiene, se usa ese ServerId: así la sincronización hace UPDATE
        // en lugar de INSERT (el INSERT chocaría con unique(id_usuario, tipo)).
        int? serverId = existente?.ServerId;
        if (serverId is null)
        {
            var todas = await LocalDatabase.ObtenerRecordatoriosAsync(usuario.Id);
            serverId = todas.FirstOrDefault(r =>
                string.Equals(r.Tipo, tipo, StringComparison.OrdinalIgnoreCase) &&
                r.ServerId is not null)?.ServerId;
        }

        await LocalDatabase.GuardarRecordatorioPendienteAsync(new RecordatorioSaludOffline
        {
            IdUsuario = usuario.Id,
            ServerId = serverId ?? existente?.ServerId,
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