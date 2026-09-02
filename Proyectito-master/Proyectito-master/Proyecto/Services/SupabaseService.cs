using System;
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
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Column("correo")]
    public string Correo { get; set; } = string.Empty;

    [Column("contrasena")]
    public string Contrasena { get; set; } = string.Empty;
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

        // Guardar el correo procesado en una variable local antes de filtrar
        var correoNormalizado = NormalizarCorreo(correo);

        var resultado = await client
            .From<Usuario>()
            .Where(u => u.Correo == correoNormalizado)
            .Get();

        var usuario = resultado.Models.FirstOrDefault();
        if (usuario is null || !PasswordHasher.Verify(contrasena, usuario.Contrasena))
            return null;

        return usuario;
    }

    public static async Task RegistrarAsync(string nombre, string correo, string contrasena)
    {
        var cliente = await GetClientAsync();
        var correoNormalizado = NormalizarCorreo(correo);

        var existentes = await cliente
            .From<Usuario>()
            .Where(u => u.Correo == correoNormalizado)
            .Get();

        if (existentes.Models.Any())
            throw new InvalidOperationException("Ese correo ya está registrado.");

        var nuevo = new Usuario
        {
            Nombre = nombre.Trim(),
            Correo = correoNormalizado,
            Contrasena = PasswordHasher.Hash(contrasena)
        };

        var respuesta = await cliente.From<Usuario>().Insert(nuevo);
        if (respuesta.Model is null)
            throw new InvalidOperationException("No se pudo crear el usuario.");
    }

    private static string NormalizarCorreo(string correo) => correo.Trim().ToLower();
}