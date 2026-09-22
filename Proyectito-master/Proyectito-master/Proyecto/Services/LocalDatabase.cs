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
    public string Estado { get; set; } = "Pendiente";
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
    // Oculta el movimiento del historial sin afectar el saldo calculado.
    public bool Oculto { get; set; }
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
    public string Rol { get; set; } = "usuario";
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

    // Bases de instalaciones antiguas pueden tener columnas con otro nombre
    // (p. ej. "id_usuario" en minúsculas) y romper las consultas con
    // "no such column". Las tablas con esquema desactualizado se recrean:
    // el próximo pull de SyncService vuelve a descargar los datos reales.
    private class ColumnaInfo
    {
        [SQLite.Column("name")]
        public string Nombre { get; set; } = "";
    }

    private static async Task RepararEsquemaAsync(SQLiteAsyncConnection conexion)
    {
        var requeridas = new (string tabla, string columna)[]
        {
            ("tarea_offline", "IdUsuario"),
            ("movimiento_offline", "IdUsuario"),
            ("movimiento_offline", "Oculto"),
            ("contrasena_offline", "IdUsuario"),
            ("recordatorio_offline", "IdUsuario"),
        };

        foreach (var (tabla, columna) in requeridas)
        {
            try
            {
                var actuales = await conexion.QueryAsync<ColumnaInfo>($"PRAGMA table_info({tabla})");
                if (actuales.All(c => !string.Equals(c.Nombre, columna, StringComparison.OrdinalIgnoreCase)))
                    await conexion.ExecuteAsync($"DROP TABLE IF EXISTS {tabla}");
            }
            catch
            {
                // Tabla aún sin crear o ya válida: no requiere reparación.
            }
        }
    }

    private static async Task<SQLiteAsyncConnection> CrearConexionAsync()
    {
        var ruta = Path.Combine(FileSystem.AppDataDirectory, NombreDb);
        var conexion = new SQLiteAsyncConnection(ruta,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

        await RepararEsquemaAsync(conexion);

        await conexion.CreateTableAsync<TareaOffline>();
        await conexion.CreateTableAsync<MovimientoOffline>();
        await conexion.CreateTableAsync<ContrasenaOffline>();
        await conexion.CreateTableAsync<RecordatorioSaludOffline>();
        await conexion.CreateTableAsync<UsuarioOffline>();

        // Migración: columna de rol en credenciales cacheadas.
        try
        {
            await conexion.ExecuteAsync("ALTER TABLE usuario_offline ADD COLUMN rol TEXT NOT NULL DEFAULT 'usuario'");
        }
        catch
        {
            // La columna ya existe (base nueva con el modelo actualizado).
        }

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

        // Migración: columna para ocultar movimientos del historial sin tocar el saldo.
        try
        {
            await conexion.ExecuteAsync("ALTER TABLE movimiento_offline ADD COLUMN oculto INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // La columna ya existe (base nueva con el modelo actualizado).
        }
        try
        {
            await conexion.ExecuteAsync("UPDATE movimiento_offline SET oculto = 0 WHERE oculto IS NULL");
        }
        catch
        {
            // Sin filas pendientes de corregir.
        }

        // Migración: concilia duplicados de recordatorios. Debe existir una sola
        // fila por (IdUsuario, Tipo); se conserva la que tiene ServerId (la que
        // representa al dato sincronizado) y, en caso de empate, la más reciente.
        try
        {
            var todos = await conexion.Table<RecordatorioSaludOffline>()
                .Where(r => r.SyncState != SyncStatus.Eliminado)
                .ToListAsync();
            foreach (var grupo in todos.GroupBy(r => (r.IdUsuario, r.Tipo)))
            {
                var canonicos = grupo
                    .OrderByDescending(r => r.ServerId is not null)
                    .ThenByDescending(r => r.Modificado)
                    .ToList();
                foreach (var sobrante in canonicos.Skip(1))
                    await conexion.DeleteAsync(sobrante);
            }
        }
        catch
        {
            // La tabla aún no existe o no requiere reparación.
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

    // Reasigna los datos de un "dueño" local a otro. Se usa para sanar la base
    // local cuando una sesión sin id de servidor (id nominal derivado del correo)
    // se convierte, tras un login en línea, en una sesión con id real: así los
    // movimientos/tareas/contraseñas creados offline no quedan huérfanos y
    // pueden sincronizarse a Supabase.
    public static async Task ReatribuirDuennoAsync(int idAnterior, int idNuevo)
    {
        if (idAnterior == idNuevo)
            return;

        var db = await GetConexionAsync();
        await db.ExecuteAsync("UPDATE tarea_offline SET IdUsuario = ? WHERE IdUsuario = ?", idNuevo, idAnterior);
        await db.ExecuteAsync("UPDATE movimiento_offline SET IdUsuario = ? WHERE IdUsuario = ?", idNuevo, idAnterior);
        await db.ExecuteAsync("UPDATE contrasena_offline SET IdUsuario = ? WHERE IdUsuario = ?", idNuevo, idAnterior);
        await db.ExecuteAsync("UPDATE recordatorio_offline SET IdUsuario = ? WHERE IdUsuario = ?", idNuevo, idAnterior);
    }

    // ── USUARIOS / CREDENCIALES OFFLINE ──

    public static async Task<UsuarioOffline?> ObtenerUsuarioPorCorreoAsync(string correo)
    {
        var db = await GetConexionAsync();
        return await db.Table<UsuarioOffline>().Where(u => u.Correo == correo).FirstOrDefaultAsync();
    }

    public static async Task GuardarCredencialesAsync(string correo, string hashContrasena, string nombre, int? serverId, string? authUserId, string rol)
    {
        var db = await GetConexionAsync();
        var existente = await db.Table<UsuarioOffline>().Where(u => u.Correo == correo).FirstOrDefaultAsync();
        if (existente is not null)
        {
            existente.HashContrasena = hashContrasena;
            existente.Nombre = nombre;
            existente.ServerId = serverId ?? existente.ServerId;
            existente.AuthUserId = authUserId ?? existente.AuthUserId;
            existente.Rol = string.IsNullOrWhiteSpace(rol) ? existente.Rol : rol;
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
                Rol = string.IsNullOrWhiteSpace(rol) ? "usuario" : rol,
                Modificado = DateTime.UtcNow
            });
        }
    }

    // Actualiza el rol de la caché offline (p. ej. cuando se promueve a admin).
    public static async Task ActualizarRolAsync(string correo, string rol)
    {
        if (string.IsNullOrWhiteSpace(rol))
            return;

        var db = await GetConexionAsync();
        var usuario = await db.Table<UsuarioOffline>().Where(u => u.Correo == correo).FirstOrDefaultAsync();
        if (usuario is null)
            return;

        usuario.Rol = rol;
        usuario.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(usuario);
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
        {
            // El cálculo debe hacerse FUERA de la expresión LINQ: sqlite-net-pcl
            // convierte los métodos desconocidos dentro del Where en funciones
            // SQL, lo que provocaba "no such function: idlocaldeinterfaz".
            int idLocal = IdLocalDeInterfaz(id);
            return await db.Table<TareaOffline>().Where(t => t.IdLocal == idLocal && t.IdUsuario == idUsuario).FirstOrDefaultAsync();
        }
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

    public static async Task<List<MovimientoOffline>> ObtenerHistorialAsync(int idUsuario)
    {
        var db = await GetConexionAsync();
        return await db.Table<MovimientoOffline>()
            .Where(m => m.IdUsuario == idUsuario && m.SyncState != SyncStatus.Eliminado && !m.Oculto)
            .OrderByDescending(m => m.Fecha)
            .ToListAsync();
    }

    public static async Task OcultarMovimientoAsync(MovimientoOffline movimiento)
    {
        var db = await GetConexionAsync();
        movimiento.Oculto = true;
        await db.UpdateAsync(movimiento);
    }

    public static async Task InsertarMovimientoPendienteAsync(MovimientoOffline movimiento)
    {
        var db = await GetConexionAsync();
        movimiento.IdLocal = 0;
        movimiento.SyncState = SyncStatus.Pendiente;
        movimiento.Modificado = DateTime.UtcNow;
        await db.InsertAsync(movimiento);
    }

    public static async Task ActualizarMovimientoPendienteAsync(MovimientoOffline movimiento)
    {
        var db = await GetConexionAsync();
        movimiento.SyncState = SyncStatus.Pendiente;
        movimiento.Modificado = DateTime.UtcNow;
        await db.UpdateAsync(movimiento);
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

    public static async Task<MovimientoOffline?> ObtenerMovimientoPorInterfazAsync(int idUsuario, int id)
    {
        var db = await GetConexionAsync();
        if (EsIdLocal(id))
        {
            // Cálculo fuera de la expresión LINQ (evita "no such function: idlocaldeinterfaz").
            int idLocal = IdLocalDeInterfaz(id);
            return await db.Table<MovimientoOffline>().Where(m => m.IdLocal == idLocal && m.IdUsuario == idUsuario).FirstOrDefaultAsync();
        }
        return await db.Table<MovimientoOffline>().Where(m => m.ServerId == id && m.IdUsuario == idUsuario).FirstOrDefaultAsync();
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
        {
            // Cálculo fuera de la expresión LINQ (evita "no such function: idlocaldeinterfaz").
            int idLocal = IdLocalDeInterfaz(id);
            return await db.Table<ContrasenaOffline>().Where(c => c.IdLocal == idLocal && c.IdUsuario == idUsuario).FirstOrDefaultAsync();
        }
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

            // Concilia duplicados: solo debe quedar una fila por (IdUsuario, Tipo).
            var duplicados = await db.Table<RecordatorioSaludOffline>()
                .Where(r => r.IdUsuario == existente.IdUsuario
                         && r.Tipo == existente.Tipo
                         && r.IdLocal != existente.IdLocal
                         && r.SyncState != SyncStatus.Eliminado)
                .ToListAsync();
            foreach (var duplicado in duplicados)
                await db.DeleteAsync(duplicado);
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

    // Vincula una fila local pendiente (mismo tipo, sin ServerId) con su fila
    // remota: así el próximo ciclo la sube con UPDATE en lugar de intentar un
    // INSERT que chocaría con unique(id_usuario, tipo_recordatorio).
    public static async Task VincularRecordatorioPendienteAsync(int idLocal, int serverId)
    {
        var db = await GetConexionAsync();
        var fila = await db.Table<RecordatorioSaludOffline>().Where(r => r.IdLocal == idLocal).FirstOrDefaultAsync();
        if (fila is null || fila.SyncState != SyncStatus.Pendiente)
            return;
        fila.ServerId = serverId;
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