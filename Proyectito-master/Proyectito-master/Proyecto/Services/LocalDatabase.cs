using Microsoft.Maui.Storage;
using SQLite;

namespace Proyecto.Services;

// Capa de almacenamiento local (SQLite). Fuente de verdad principal de la app:
// todas las lecturas se resuelven aquí (funciona sin conexión) y las escrituras
// se marcan como pendientes hasta que SyncService las envía a Supabase.

public static class SyncStatus
{
    public const string Sincronizada = "synced";
    public const string Pendiente = "pending";
    public const string Eliminado = "deleted";
}

// ── ENTIDADES LOCALES (espejo de las tablas de Supabase) ──

[Table("tarea_offline")]
public class TareaOffline
{
    [PrimaryKey, AutoIncrement]
    public int IdLocal { get; set; }
    public int? ServerId { get; set; }
    public int IdUsuario { get; set; }
    public string Titulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public DateTime? FechaVencimiento { get; set; }
    public string Estado { get; set; } = "pendiente";
    public string SyncState { get; set; } = SyncStatus.Sincronizada;
    public DateTime Modificado { get; set; } = DateTime.UtcNow;
}

[Table("movimiento_offline")]
public class MovimientoOffline
{
    [PrimaryKey, AutoIncrement]
    public int IdLocal { get; set; }
    public int? ServerId { get; set; }
    public int IdUsuario { get; set; }
    public decimal Monto { get; set; }
    public string Tipo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string SyncState { get; set; } = SyncStatus.Sincronizada;
    public DateTime Modificado { get; set; } = DateTime.UtcNow;
}

[Table("contrasena_offline")]
public class ContrasenaOffline
{
    [PrimaryKey, AutoIncrement]
    public int IdLocal { get; set; }
    public int? ServerId { get; set; }
    public int IdUsuario { get; set; }
    public string SitioWeb { get; set; } = "";
    public string UsuarioCuenta { get; set; } = "";
    public string ClaveCifrada { get; set; } = "";
    public string SyncState { get; set; } = SyncStatus.Sincronizada;
    public DateTime Modificado { get; set; } = DateTime.UtcNow;
}

[Table("recordatorio_offline")]
public class RecordatorioSaludOffline
{
    [PrimaryKey, AutoIncrement]
    public int IdLocal { get; set; }
    public int? ServerId { get; set; }
    public int IdUsuario { get; set; }
    public string Tipo { get; set; } = "";
    public int FrecuenciaMinutos { get; set; }
    public int FrecuenciaValor { get; set; }
    public string FrecuenciaUnidad { get; set; } = "min";
    public bool Activo { get; set; }
    public string SyncState { get; set; } = SyncStatus.Sincronizada;
    public DateTime Modificado { get; set; } = DateTime.UtcNow;
}

// Credenciales cacheadas para permitir el inicio de sesión SIN conexión.
[Table("usuario_offline")]
public class UsuarioOffline
{
    [PrimaryKey]
    public string Correo { get; set; } = "";
    public int? ServerId { get; set; }
    public string Nombre { get; set; } = "";
    public string? AuthUserId { get; set; }
    public string HashContrasena { get; set; } = "";
    public DateTime Modificado { get; set; } = DateTime.UtcNow;
}

// ── BASE DE DATOS LOCAL ──

public static class LocalDatabase
{
    private const string NombreDb = "proyecto_offline.db3";
    private static readonly SemaphoreSlim CreacionLock = new(1, 1);
    private static SQLiteAsyncConnection? _conexion;

    public static async Task<SQLiteAsyncConnection> GetConexionAsync()
    {
        if (_conexion is not null)
            return _conexion;

        await CreacionLock.WaitAsync();
        try
        {
            _conexion ??= await CrearConexionAsync();
        }
        finally
        {
            CreacionLock.Release();
        }

        return _conexion;
    }

    private static async Task<SQLiteAsyncConnection> CrearConexionAsync()
    {
        var ruta = Path.Combine(FileSystem.AppDataDirectory, NombreDb);
        var conexion = new SQLiteAsyncConnection(ruta,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

        await conexion.CreateTableAsync<TareaOffline>();
        await conexion.CreateTableAsync<MovimientoOffline>();
        await conexion.CreateTableAsync<ContrasenaOffline>();
        await conexion.CreateTableAsync<RecordatorioSaludOffline>();
        await conexion.CreateTableAsync<UsuarioOffline>();

        // Migración: agrega las columnas de unidad de frecuencia a bases existentes.
        try
        {
            await conexion.ExecuteAsync("ALTER TABLE recordatorio_offline ADD COLUMN frecuencia_valor INTEGER NOT NULL DEFAULT 0");
            await conexion.ExecuteAsync("ALTER TABLE recordatorio_offline ADD COLUMN frecuencia_unidad TEXT NOT NULL DEFAULT 'min'");
        }
        catch
        {
            // Las columnas ya existen (base nueva o migración previa).
        }

        return conexion;
    }

    // ── MAPEO de/para Supabase (los modelos remotos están en SupabaseService.cs) ──

    // NOTA: los IDs que se exponen a la interfaz usan una convención:
    //   - ServerId presente  → se devuelve el id real de Supabase (positivo).
    //   - ServerId nulo (creado offline) → se devuelve un id local negativo (-IdLocal)
    //     para poder actualizar/eliminar esa fila sin depender del servidor.
    public static int IdInterfaz(int? serverId, int idLocal) => serverId ?? -idLocal;
    private static bool EsIdLocal(int id) => id < 0;
    private static int IdLocalDeInterfaz(int id) => -id;

    // ── USUARIOS / CREDENCIALES OFFLINE ──

    public static async Task<UsuarioOffline?> ObtenerUsuarioPorCorreoAsync(string correo)
    {
        var db = await GetConexionAsync();
        return await db.Table<UsuarioOffline>().Where(u => u.Correo == correo).FirstOrDefaultAsync();
    }

    public static async Task GuardarCredencialesAsync(string correo, string hashContrasena, string nombre, int? serverId, string? authUserId)
    {
        var db = await GetConexionAsync();
        var existente = await db.Table<UsuarioOffline>().Where(u => u.Correo == correo).FirstOrDefaultAsync();
        if (existente is not null)
        {
            existente.HashContrasena = hashContrasena;
            existente.Nombre = nombre;
            existente.ServerId = serverId ?? existente.ServerId;
            existente.AuthUserId = authUserId ?? existente.AuthUserId;
            existente.Modificado = DateTime.UtcNow;
            await db.UpdateAsync(existente);
        }
        else
        {
            await db.InsertAsync(new UsuarioOffline
            {
                Correo = correo,
                HashContrasena = hashContrasena,
                Nombre = nombre,
                ServerId = serverId,
                AuthUserId = authUserId,
                Modificado = DateTime.UtcNow
            });
        }
    }

    // ── TAREAS ──

    public static async Task<List<TareaOffline>> ObtenerTareasAsync(int idUsuario)
    {
        var db = await GetConexionAsync();
        return await db.Table<TareaOffline>()
            .Where(t => t.IdUsuario == idUsuario && t.SyncState != SyncStatus.Eliminado)
            .OrderByDescending(t => t.IdLocal)
            .ToListAsync();
    }

    public static async Task<TareaOffline?> ObtenerTareaPorInterfazAsync(int idUsuario, int id)
    {
        var db = await GetConexionAsync();
        if (EsIdLocal(id))
            return await db.Table<TareaOffline>().Where(t => t.IdLocal == IdLocalDeInterfaz(id) && t.IdUsuario == idUsuario).FirstOrDefaultAsync();
        return await db.Table<TareaOffline>().Where(t => t.ServerId == id && t.IdUsuario == idUsuario).FirstOrDefaultAsync();
    }

    public static async Task InsertarTareaPendienteAsync(TareaOffline tarea)
    {
        var db = await GetConexionAsync();
        tarea.IdLocal = 0;
        tarea.SyncState = SyncStatus.Pendiente;
        tarea.Modificado = DateTime.UtcNow;
        await db.InsertAsync(tarea);
    }

    public static async Task ActualizarTareaPendienteAsync(TareaOffline tarea)
    {
        var db = await GetConexionAsync();
        tarea.SyncState = SyncStatus.Pendiente;
        tarea.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(tarea);
    }

    public static async Task MarcarTareaEliminadaAsync(TareaOffline tarea)
    {
        var db = await GetConexionAsync();
        if (tarea.ServerId is null)
        {
            // Nunca se sincronizó: basta con borrarla localmente y ya.
            await db.DeleteAsync(tarea);
            return;
        }
        tarea.SyncState = SyncStatus.Eliminado;
        tarea.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(tarea);
    }

    public static async Task<List<TareaOffline>> TareasPorSyncStateAsync(int idUsuario, string estado)
    {
        var db = await GetConexionAsync();
        return await db.Table<TareaOffline>()
            .Where(t => t.IdUsuario == idUsuario && t.SyncState == estado)
            .ToListAsync();
    }

    public static async Task MarcarTareaSincronizadaAsync(int idLocal, int serverId)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<TareaOffline>().Where(t => t.IdLocal == idLocal).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.ServerId = serverId;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task ActualizarTareaDesdeServidorAsync(TareaOffline actualizada)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<TareaOffline>().Where(t => t.ServerId == actualizada.ServerId).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.Titulo = actualizada.Titulo;
        fila.Descripcion = actualizada.Descripcion;
        fila.FechaVencimiento = actualizada.FechaVencimiento;
        fila.Estado = actualizada.Estado;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task BorrarTareaLocalAsync(int idLocal)
    {
        var db = await GetConexionAsync();
        await db.DeleteAsync<TareaOffline>(idLocal);
    }

    // ── MOVIMIENTOS FINANCIEROS ──

    public static async Task<List<MovimientoOffline>> ObtenerMovimientosAsync(int idUsuario)
    {
        var db = await GetConexionAsync();
        return await db.Table<MovimientoOffline>()
            .Where(m => m.IdUsuario == idUsuario && m.SyncState != SyncStatus.Eliminado)
            .OrderByDescending(m => m.Fecha)
            .ToListAsync();
    }

    public static async Task InsertarMovimientoPendienteAsync(MovimientoOffline movimiento)
    {
        var db = await GetConexionAsync();
        movimiento.IdLocal = 0;
        movimiento.SyncState = SyncStatus.Pendiente;
        movimiento.Modificado = DateTime.UtcNow;
        await db.InsertAsync(movimiento);
    }

    public static async Task<List<MovimientoOffline>> MovimientosPorSyncStateAsync(int idUsuario, string estado)
    {
        var db = await GetConexionAsync();
        return await db.Table<MovimientoOffline>()
            .Where(m => m.IdUsuario == idUsuario && m.SyncState == estado)
            .ToListAsync();
    }

    public static async Task MarcarMovimientoSincronizadoAsync(int idLocal, int serverId)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<MovimientoOffline>().Where(m => m.IdLocal == idLocal).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.ServerId = serverId;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task ActualizarMovimientoDesdeServidorAsync(MovimientoOffline actualizado)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<MovimientoOffline>().Where(m => m.ServerId == actualizado.ServerId).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.Monto = actualizado.Monto;
        fila.Tipo = actualizado.Tipo;
        fila.Descripcion = actualizado.Descripcion;
        fila.Fecha = actualizado.Fecha;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task BorrarMovimientoLocalAsync(int idLocal)
    {
        var db = await GetConexionAsync();
        await db.DeleteAsync<MovimientoOffline>(idLocal);
    }

    // ── CONTRASEÑAS ──

    public static async Task<List<ContrasenaOffline>> ObtenerContrasenasAsync(int idUsuario)
    {
        var db = await GetConexionAsync();
        return await db.Table<ContrasenaOffline>()
            .Where(c => c.IdUsuario == idUsuario && c.SyncState != SyncStatus.Eliminado)
            .OrderByDescending(c => c.IdLocal)
            .ToListAsync();
    }

    public static async Task<ContrasenaOffline?> ObtenerContrasenaPorInterfazAsync(int idUsuario, int id)
    {
        var db = await GetConexionAsync();
        if (EsIdLocal(id))
            return await db.Table<ContrasenaOffline>().Where(c => c.IdLocal == IdLocalDeInterfaz(id) && c.IdUsuario == idUsuario).FirstOrDefaultAsync();
        return await db.Table<ContrasenaOffline>().Where(c => c.ServerId == id && c.IdUsuario == idUsuario).FirstOrDefaultAsync();
    }

    public static async Task InsertarContrasenaPendienteAsync(ContrasenaOffline contrasena)
    {
        var db = await GetConexionAsync();
        contrasena.IdLocal = 0;
        contrasena.SyncState = SyncStatus.Pendiente;
        contrasena.Modificado = DateTime.UtcNow;
        await db.InsertAsync(contrasena);
    }

    public static async Task ActualizarContrasenaPendienteAsync(ContrasenaOffline contrasena)
    {
        var db = await GetConexionAsync();
        contrasena.SyncState = SyncStatus.Pendiente;
        contrasena.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(contrasena);
    }

    public static async Task MarcarContrasenaEliminadaAsync(ContrasenaOffline contrasena)
    {
        var db = await GetConexionAsync();
        if (contrasena.ServerId is null)
        {
            await db.DeleteAsync(contrasena);
            return;
        }
        contrasena.SyncState = SyncStatus.Eliminado;
        contrasena.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(contrasena);
    }

    public static async Task<List<ContrasenaOffline>> ContrasenasPorSyncStateAsync(int idUsuario, string estado)
    {
        var db = await GetConexionAsync();
        return await db.Table<ContrasenaOffline>()
            .Where(c => c.IdUsuario == idUsuario && c.SyncState == estado)
            .ToListAsync();
    }

    public static async Task MarcarContrasenaSincronizadaAsync(int idLocal, int serverId)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<ContrasenaOffline>().Where(c => c.IdLocal == idLocal).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.ServerId = serverId;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task ActualizarContrasenaDesdeServidorAsync(ContrasenaOffline actualizada)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<ContrasenaOffline>().Where(c => c.ServerId == actualizada.ServerId).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.SitioWeb = actualizada.SitioWeb;
        fila.UsuarioCuenta = actualizada.UsuarioCuenta;
        fila.ClaveCifrada = actualizada.ClaveCifrada;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task BorrarContrasenaLocalAsync(int idLocal)
    {
        var db = await GetConexionAsync();
        await db.DeleteAsync<ContrasenaOffline>(idLocal);
    }

    // ── RECORDATORIOS DE SALUD ──

    public static async Task<List<RecordatorioSaludOffline>> ObtenerRecordatoriosAsync(int idUsuario)
    {
        var db = await GetConexionAsync();
        return await db.Table<RecordatorioSaludOffline>()
            .Where(r => r.IdUsuario == idUsuario && r.SyncState != SyncStatus.Eliminado)
            .ToListAsync();
    }

    public static async Task<RecordatorioSaludOffline?> ObtenerRecordatorioPorTipoAsync(int idUsuario, string tipo)
    {
        var db = await GetConexionAsync();
        return await db.Table<RecordatorioSaludOffline>()
            .Where(r => r.IdUsuario == idUsuario && r.Tipo == tipo && r.SyncState != SyncStatus.Eliminado)
            .FirstOrDefaultAsync();
    }

    public static async Task GuardarRecordatorioPendienteAsync(RecordatorioSaludOffline recordatorio)
    {
        var db = await GetConexionAsync();
        var existente = await db.Table<RecordatorioSaludOffline>()
            .Where(r => r.IdUsuario == recordatorio.IdUsuario && r.Tipo == recordatorio.Tipo)
            .FirstOrDefaultAsync();

        if (existente is not null)
        {
            existente.FrecuenciaMinutos = recordatorio.FrecuenciaMinutos;
            existente.FrecuenciaValor = recordatorio.FrecuenciaValor;
            existente.FrecuenciaUnidad = recordatorio.FrecuenciaUnidad;
            existente.Activo = recordatorio.Activo;
            existente.SyncState = SyncStatus.Pendiente;
            existente.Modificado = DateTime.UtcNow;
            await db.UpdateAsync(existente);
        }
        else
        {
            recordatorio.IdLocal = 0;
            recordatorio.SyncState = recordatorio.ServerId is null ? SyncStatus.Pendiente : SyncStatus.Sincronizada;
            recordatorio.Modificado = DateTime.UtcNow;
            await db.InsertAsync(recordatorio);
        }
    }

    public static async Task<List<RecordatorioSaludOffline>> RecordatoriosPorSyncStateAsync(int idUsuario, string estado)
    {
        var db = await GetConexionAsync();
        return await db.Table<RecordatorioSaludOffline>()
            .Where(r => r.IdUsuario == idUsuario && r.SyncState == estado)
            .ToListAsync();
    }

    public static async Task MarcarRecordatorioSincronizadoAsync(int idLocal, int serverId)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<RecordatorioSaludOffline>().Where(r => r.IdLocal == idLocal).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.ServerId = serverId;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task ActualizarRecordatorioDesdeServidorAsync(RecordatorioSaludOffline actualizado)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<RecordatorioSaludOffline>().Where(r => r.ServerId == actualizado.ServerId).FirstOrDefaultAsync();
        if (fila is null) return;
        fila.FrecuenciaMinutos = actualizado.FrecuenciaMinutos;
        fila.FrecuenciaValor = actualizado.FrecuenciaValor;
        fila.FrecuenciaUnidad = actualizado.FrecuenciaUnidad;
        fila.Activo = actualizado.Activo;
        fila.SyncState = SyncStatus.Sincronizada;
        fila.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(fila);
    }

    public static async Task MarcarRecordatorioEliminadoAsync(RecordatorioSaludOffline recordatorio)
    {
        var db = await GetConexionAsync();
        if (recordatorio.ServerId is null)
        {
            await db.DeleteAsync(recordatorio);
            return;
        }
        recordatorio.SyncState = SyncStatus.Eliminado;
        recordatorio.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(recordatorio);
    }

    public static async Task BorrarRecordatorioLocalAsync(int idLocal)
    {
        var db = await GetConexionAsync();
        await db.DeleteAsync<RecordatorioSaludOffline>(idLocal);
    }
}