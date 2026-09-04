using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Proyecto.Services;

// 1. ESTRUCTURA DE LA TABLA (Modelo)
[Table("usuario")]
public class Usuario : BaseModel
{
    [PrimaryKey("id_usuario", false)]
    [Column("id_usuario")]
    public int Id { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Column("correo")]
    public string Correo { get; set; } = string.Empty;

    [Column("auth_user_id")]
    public string? AuthUserId { get; set; }
}

// MODELO: MOVIMIENTO FINANCIERO
[Table("movimiento_financiero")]
public class MovimientoFinanciero : BaseModel
{
    [PrimaryKey("id_movimiento", false)]
    [Column("id_movimiento")]
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

// MODELO: CONTRASEÑA
[Table("contrasenas")]
public class Contrasena : BaseModel
{
    [PrimaryKey("id_contrasena", false)]
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

// MODELO: TAREA
[Table("tarea")]
public class Tarea : BaseModel
{
    [PrimaryKey("id_tarea", false)]
    [Column("id_tarea")]
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

// 4. OPERACIONES CON LA BASE DE DATOS
public static class SupabaseService
{
    private static Client? _client;
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    // Sesión del usuario actual
    public static Usuario? UsuarioActual { get; private set; }

    public static void EstablecerSesion(Usuario usuario) => UsuarioActual = usuario;
    public static void CerrarSesion() => UsuarioActual = null;

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

    public static async Task<Usuario?> LoginAsync(string correo, string contrasena)
    {
        var client = await GetClientAsync();
        var correoNormalizado = NormalizarCorreo(correo);

        try
        {
            await client.Auth.SignIn(correoNormalizado, contrasena);
        }
        catch (Exception)
        {
            return null;
        }

        var resultado = await client
            .From<Usuario>()
            .Where(u => u.Correo == correoNormalizado)
            .Get();

        var usuario = resultado.Models.FirstOrDefault();
        if (usuario is null)
            return null;

        EstablecerSesion(usuario);
        return usuario;
    }

    public static async Task RegistrarAsync(string nombre, string correo, string contrasena)
    {
        var cliente = await GetClientAsync();
        var correoNormalizado = NormalizarCorreo(correo);

        try
        {
            await cliente.Auth.SignUp(correoNormalizado, contrasena);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No se pudo crear la cuenta: {ex.Message}");
        }

        var usuarioAutenticado = cliente.Auth.CurrentUser;

        var nuevo = new Usuario
        {
            Nombre = nombre.Trim(),
            Correo = correoNormalizado,
            AuthUserId = usuarioAutenticado?.Id
        };

        var respuesta = await cliente.From<Usuario>().Insert(nuevo);
        var usuarioCreado = respuesta.Models.FirstOrDefault();

        if (usuarioCreado is not null)
        {
            EstablecerSesion(usuarioCreado);
        }
    }

    private static string NormalizarCorreo(string correo) => correo.Trim().ToLower();

    // ── MOVIMIENTOS FINANCIEROS ──

    public static async Task<decimal> ObtenerSaldoAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return 0;

        var client = await GetClientAsync();
        var resultado = await client
            .From<MovimientoFinanciero>()
            .Where(m => m.IdUsuario == usuario.Id)
            .Get();

        decimal saldo = 0;
        foreach (var m in resultado.Models)
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

        var client = await GetClientAsync();
        var resultado = await client
            .From<MovimientoFinanciero>()
            .Where(m => m.IdUsuario == usuario.Id)
            .Order(m => m.Fecha, Supabase.Postgrest.Constants.Ordering.Descending)
            .Get();

        return resultado.Models;
    }

    public static async Task RegistrarMovimientoAsync(decimal monto, string tipo, string descripcion)
    {
        var usuario = UsuarioActual;

        if (usuario is null || usuario.Id <= 0)
            throw new InvalidOperationException("No hay una sesión activa con ID de usuario válido.");

        var client = await GetClientAsync();
        var movimiento = new MovimientoFinanciero
        {
            IdUsuario = usuario.Id,
            Monto = monto,
            Tipo = tipo,
            Descripcion = descripcion.Trim(),
            Fecha = DateTime.UtcNow
        };

        await client.From<MovimientoFinanciero>().Insert(movimiento);
    }

    // ── CONTRASEÑAS (CRUD) ──

    public static async Task<List<Contrasena>> ObtenerContrasenasAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return new List<Contrasena>();

        var client = await GetClientAsync();
        var resultado = await client
            .From<Contrasena>()
            .Where(c => c.IdUsuario == usuario.Id)
            .Get();

        return resultado.Models;
    }

    public static async Task CrearContrasenaAsync(string sitioWeb, string usuarioCuenta, string clave)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            throw new InvalidOperationException("No hay sesión activa.");

        var client = await GetClientAsync();
        var nueva = new Contrasena
        {
            IdUsuario = usuario.Id,
            SitioWeb = sitioWeb.Trim(),
            UsuarioCuenta = usuarioCuenta.Trim(),
            ClaveCifrada = PasswordHasher.Hash(clave)
        };

        await client.From<Contrasena>().Insert(nueva);
    }

    public static async Task ActualizarContrasenaAsync(int idContrasena, string sitioWeb, string usuarioCuenta, string clave)
    {
        var client = await GetClientAsync();

        var query = client.From<Contrasena>().Where(c => c.IdContrasena == idContrasena);
        query = query.Set(c => c.SitioWeb, sitioWeb.Trim());
        query = query.Set(c => c.UsuarioCuenta, usuarioCuenta.Trim());

        if (!string.IsNullOrWhiteSpace(clave))
            query = query.Set(c => c.ClaveCifrada, PasswordHasher.Hash(clave));

        await query.Update();
    }

    public static async Task EliminarContrasenaAsync(int idContrasena)
    {
        var client = await GetClientAsync();
        await client
            .From<Contrasena>()
            .Where(c => c.IdContrasena == idContrasena)
            .Delete();
    }

    // ── TAREAS (CRUD) ──

    public static async Task<List<Tarea>> ObtenerTareasAsync()
    {
        var usuario = UsuarioActual;
        if (usuario is null) return new List<Tarea>();

        var client = await GetClientAsync();
        var resultado = await client
            .From<Tarea>()
            .Where(t => t.IdUsuario == usuario.Id)
            .Order(t => t.IdTarea, Supabase.Postgrest.Constants.Ordering.Descending)
            .Get();

        return resultado.Models;
    }

    public static async Task CrearTareaAsync(string titulo, string descripcion, DateTime? fechaVencimiento, string estado)
    {
        var usuario = UsuarioActual;
        if (usuario is null)
            throw new InvalidOperationException("No hay sesión activa.");

        var client = await GetClientAsync();
        var nueva = new Tarea
        {
            IdUsuario = usuario.Id,
            Titulo = titulo.Trim(),
            Descripcion = descripcion?.Trim() ?? "",
            FechaVencimiento = fechaVencimiento,
            Estado = string.IsNullOrWhiteSpace(estado) ? "pendiente" : estado.Trim()
        };

        await client.From<Tarea>().Insert(nueva);
    }

    public static async Task ActualizarTareaAsync(int idTarea, string titulo, string descripcion, DateTime? fechaVencimiento, string estado)
    {
        var client = await GetClientAsync();

        var query = client.From<Tarea>().Where(t => t.IdTarea == idTarea);
        query = query.Set(t => t.Titulo, titulo.Trim());
        query = query.Set(t => t.Descripcion, descripcion?.Trim() ?? "");
        query = query.Set(t => t.FechaVencimiento, fechaVencimiento);
        query = query.Set(t => t.Estado, string.IsNullOrWhiteSpace(estado) ? "pendiente" : estado.Trim());

        await query.Update();
    }

    public static async Task CambiarEstadoTareaAsync(int idTarea, string estado)
    {
        var client = await GetClientAsync();
        await client
            .From<Tarea>()
            .Where(t => t.IdTarea == idTarea)
            .Set(t => t.Estado, estado.Trim())
            .Update();
    }

    public static async Task EliminarTareaAsync(int idTarea)
    {
        var client = await GetClientAsync();
        await client
            .From<Tarea>()
            .Where(t => t.IdTarea == idTarea)
            .Delete();
    }
}