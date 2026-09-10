using Microsoft.Maui.Networking;
using Supabase;

namespace Proyecto.Services;

// Motor de sincronización local ↔ Supabase.
//
//  - PUSH: envía primero las filas locales pendientes (insert/update/delete).
//  - PULL: descarga después los registros del servidor y los combina,
//          respetando los posibles cambios locales aún sin enviar.
public static class SyncService
{
    public static bool Conectado => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    private static readonly SemaphoreSlim LockSincronizacion = new(1, 1);

    public static async Task SincronizarAsync()
    {
        if (!Conectado)
            return;

        if (SupabaseService.UsuarioActual is null)
            return;

        if (!await LockSincronizacion.WaitAsync(0))
            return; // Ya hay una sincronización en curso.

        try
        {
            var client = await SupabaseService.GetClientAsync();
            int idUsuario = SupabaseService.UsuarioActual.Id;

            await SincronizarTareasAsync(client, idUsuario);
            await SincronizarMovimientosAsync(client, idUsuario);
            await SincronizarContrasenasAsync(client, idUsuario);
            await SincronizarRecordatoriosAsync(client, idUsuario);
        }
        catch
        {
            // Sin conexión efectiva o error del servidor:
            // los cambios locales quedan pendientes para el próximo intento.
        }
        finally
        {
            LockSincronizacion.Release();
        }
    }

    // ── TAREAS ──

    private static async Task SincronizarTareasAsync(Client client, int idUsuario)
    {
        var db = await LocalDatabase.GetConexionAsync();

        var pendientes = await LocalDatabase.TareasPorSyncStateAsync(idUsuario, SyncStatus.Pendiente);
        foreach (var local in pendientes)
        {
            if (local.ServerId is int serverId)
            {
                var q = client.From<Tarea>().Where(t => t.IdTarea == serverId);
                q = q.Set(t => t.Titulo, local.Titulo)
                     .Set(t => t.Descripcion, local.Descripcion)
                     .Set(t => t.FechaVencimiento, local.FechaVencimiento)
                     .Set(t => t.Estado, local.Estado);
                await q.Update();
                await LocalDatabase.MarcarTareaSincronizadaAsync(local.IdLocal, serverId);
            }
            else
            {
                var creado = await client.From<Tarea>().Insert(new Tarea
                {
                    IdUsuario = idUsuario,
                    Titulo = local.Titulo,
                    Descripcion = local.Descripcion,
                    FechaVencimiento = local.FechaVencimiento,
                    Estado = local.Estado
                });
                var remoto = creado.Models.FirstOrDefault();
                if (remoto is not null)
                    await LocalDatabase.MarcarTareaSincronizadaAsync(local.IdLocal, remoto.IdTarea);
            }
        }

        var eliminadas = await LocalDatabase.TareasPorSyncStateAsync(idUsuario, SyncStatus.Eliminado);
        foreach (var local in eliminadas)
        {
            if (local.ServerId is int serverId)
            {
                try { await client.From<Tarea>().Where(t => t.IdTarea == serverId).Delete(); } catch { }
            }
            await LocalDatabase.BorrarTareaLocalAsync(local.IdLocal);
        }

        await TraerTareasAsync(client, idUsuario);
    }

    private static async Task TraerTareasAsync(Client client, int idUsuario)
    {
        var remotos = await client.From<Tarea>().Where(t => t.IdUsuario == idUsuario).Get();
        var locales = await LocalDatabase.TareasPorSyncStateAsync(idUsuario, SyncStatus.Sincronizada);

        foreach (var r in remotos.Models)
        {
            var local = locales.FirstOrDefault(l => l.ServerId == r.IdTarea);
            if (local is null)
            {
                var db = await LocalDatabase.GetConexionAsync();
                await db.InsertAsync(new TareaOffline
                {
                    ServerId = r.IdTarea,
                    IdUsuario = idUsuario,
                    Titulo = r.Titulo,
                    Descripcion = r.Descripcion,
                    FechaVencimiento = r.FechaVencimiento,
                    Estado = r.Estado,
                    SyncState = SyncStatus.Sincronizada,
                    Modificado = DateTime.UtcNow
                });
            }
            else
            {
                await LocalDatabase.ActualizarTareaDesdeServidorAsync(new TareaOffline
                {
                    ServerId = r.IdTarea,
                    Titulo = r.Titulo,
                    Descripcion = r.Descripcion,
                    FechaVencimiento = r.FechaVencimiento,
                    Estado = r.Estado
                });
            }
        }

        var idsRemotos = remotos.Models.Select(r => r.IdTarea).ToHashSet();
        foreach (var local in locales)
        {
            if (local.ServerId is int serverId && !idsRemotos.Contains(serverId))
                await LocalDatabase.BorrarTareaLocalAsync(local.IdLocal);
        }
    }

    // ── MOVIMIENTOS FINANCIEROS ──

    private static async Task SincronizarMovimientosAsync(Client client, int idUsuario)
    {
        var pendientes = await LocalDatabase.MovimientosPorSyncStateAsync(idUsuario, SyncStatus.Pendiente);
        foreach (var local in pendientes)
        {
            if (local.ServerId is int serverId)
            {
                var q = client.From<MovimientoFinanciero>().Where(m => m.IdMovimiento == serverId);
                q = q.Set(m => m.Monto, local.Monto)
                     .Set(m => m.Tipo, local.Tipo)
                     .Set(m => m.Descripcion, local.Descripcion)
                     .Set(m => m.Fecha, local.Fecha);
                await q.Update();
                await LocalDatabase.MarcarMovimientoSincronizadoAsync(local.IdLocal, serverId);
            }
            else
            {
                var creado = await client.From<MovimientoFinanciero>().Insert(new MovimientoFinanciero
                {
                    IdUsuario = idUsuario,
                    Monto = local.Monto,
                    Tipo = local.Tipo,
                    Descripcion = local.Descripcion,
                    Fecha = local.Fecha
                });
                var remoto = creado.Models.FirstOrDefault();
                if (remoto is not null)
                    await LocalDatabase.MarcarMovimientoSincronizadoAsync(local.IdLocal, remoto.IdMovimiento);
            }
        }

        var eliminados = await LocalDatabase.MovimientosPorSyncStateAsync(idUsuario, SyncStatus.Eliminado);
        foreach (var local in eliminados)
        {
            if (local.ServerId is int serverId)
            {
                try { await client.From<MovimientoFinanciero>().Where(m => m.IdMovimiento == serverId).Delete(); } catch { }
            }
            await LocalDatabase.BorrarMovimientoLocalAsync(local.IdLocal);
        }

        await TraerMovimientosAsync(client, idUsuario);
    }

    private static async Task TraerMovimientosAsync(Client client, int idUsuario)
    {
        var remotos = await client.From<MovimientoFinanciero>().Where(m => m.IdUsuario == idUsuario).Get();
        var locales = await LocalDatabase.MovimientosPorSyncStateAsync(idUsuario, SyncStatus.Sincronizada);

        foreach (var r in remotos.Models)
        {
            var local = locales.FirstOrDefault(l => l.ServerId == r.IdMovimiento);
            if (local is null)
            {
                var db = await LocalDatabase.GetConexionAsync();
                await db.InsertAsync(new MovimientoOffline
                {
                    ServerId = r.IdMovimiento,
                    IdUsuario = idUsuario,
                    Monto = r.Monto,
                    Tipo = r.Tipo,
                    Descripcion = r.Descripcion,
                    Fecha = r.Fecha,
                    SyncState = SyncStatus.Sincronizada,
                    Modificado = DateTime.UtcNow
                });
            }
            else
            {
                await LocalDatabase.ActualizarMovimientoDesdeServidorAsync(new MovimientoOffline
                {
                    ServerId = r.IdMovimiento,
                    Monto = r.Monto,
                    Tipo = r.Tipo,
                    Descripcion = r.Descripcion,
                    Fecha = r.Fecha
                });
            }
        }

        var idsRemotos = remotos.Models.Select(m => m.IdMovimiento).ToHashSet();
        foreach (var local in locales)
        {
            if (local.ServerId is int serverId && !idsRemotos.Contains(serverId))
                await LocalDatabase.BorrarMovimientoLocalAsync(local.IdLocal);
        }
    }

    // ── CONTRASEÑAS ──

    private static async Task SincronizarContrasenasAsync(Client client, int idUsuario)
    {
        var pendientes = await LocalDatabase.ContrasenasPorSyncStateAsync(idUsuario, SyncStatus.Pendiente);
        foreach (var local in pendientes)
        {
            if (local.ServerId is int serverId)
            {
                var q = client.From<Contrasena>().Where(c => c.IdContrasena == serverId);
                q = q.Set(c => c.SitioWeb, local.SitioWeb)
                     .Set(c => c.UsuarioCuenta, local.UsuarioCuenta)
                     .Set(c => c.ClaveCifrada, local.ClaveCifrada);
                await q.Update();
                await LocalDatabase.MarcarContrasenaSincronizadaAsync(local.IdLocal, serverId);
            }
            else
            {
                var creado = await client.From<Contrasena>().Insert(new Contrasena
                {
                    IdUsuario = idUsuario,
                    SitioWeb = local.SitioWeb,
                    UsuarioCuenta = local.UsuarioCuenta,
                    ClaveCifrada = local.ClaveCifrada
                });
                var remoto = creado.Models.FirstOrDefault();
                if (remoto is not null)
                    await LocalDatabase.MarcarContrasenaSincronizadaAsync(local.IdLocal, remoto.IdContrasena);
            }
        }

        var eliminadas = await LocalDatabase.ContrasenasPorSyncStateAsync(idUsuario, SyncStatus.Eliminado);
        foreach (var local in eliminadas)
        {
            if (local.ServerId is int serverId)
            {
                try { await client.From<Contrasena>().Where(c => c.IdContrasena == serverId).Delete(); } catch { }
            }
            await LocalDatabase.BorrarContrasenaLocalAsync(local.IdLocal);
        }

        await TraerContrasenasAsync(client, idUsuario);
    }

    private static async Task TraerContrasenasAsync(Client client, int idUsuario)
    {
        var remotos = await client.From<Contrasena>().Where(c => c.IdUsuario == idUsuario).Get();
        var locales = await LocalDatabase.ContrasenasPorSyncStateAsync(idUsuario, SyncStatus.Sincronizada);

        foreach (var r in remotos.Models)
        {
            var local = locales.FirstOrDefault(l => l.ServerId == r.IdContrasena);
            if (local is null)
            {
                var db = await LocalDatabase.GetConexionAsync();
                await db.InsertAsync(new ContrasenaOffline
                {
                    ServerId = r.IdContrasena,
                    IdUsuario = idUsuario,
                    SitioWeb = r.SitioWeb,
                    UsuarioCuenta = r.UsuarioCuenta,
                    ClaveCifrada = r.ClaveCifrada,
                    SyncState = SyncStatus.Sincronizada,
                    Modificado = DateTime.UtcNow
                });
            }
            else
            {
                await LocalDatabase.ActualizarContrasenaDesdeServidorAsync(new ContrasenaOffline
                {
                    ServerId = r.IdContrasena,
                    SitioWeb = r.SitioWeb,
                    UsuarioCuenta = r.UsuarioCuenta,
                    ClaveCifrada = r.ClaveCifrada
                });
            }
        }

        var idsRemotos = remotos.Models.Select(c => c.IdContrasena).ToHashSet();
        foreach (var local in locales)
        {
            if (local.ServerId is int serverId && !idsRemotos.Contains(serverId))
                await LocalDatabase.BorrarContrasenaLocalAsync(local.IdLocal);
        }
    }

    // ── RECORDATORIOS DE SALUD ──

    private static async Task SincronizarRecordatoriosAsync(Client client, int idUsuario)
    {
        var pendientes = await LocalDatabase.RecordatoriosPorSyncStateAsync(idUsuario, SyncStatus.Pendiente);
        foreach (var local in pendientes)
        {
            if (local.ServerId is int serverId)
            {
                var q = client.From<RecordatorioSalud>().Where(r => r.Id == serverId);
                q = q.Set(r => r.Tipo, local.Tipo)
                     .Set(r => r.FrecuenciaMinutos, local.FrecuenciaMinutos)
                     .Set(r => r.FrecuenciaValor, local.FrecuenciaValor)
                     .Set(r => r.FrecuenciaUnidad, local.FrecuenciaUnidad)
                     .Set(r => r.Activo, local.Activo);
                await q.Update();
                await LocalDatabase.MarcarRecordatorioSincronizadoAsync(local.IdLocal, serverId);
            }
            else
            {
                var creado = await client.From<RecordatorioSalud>().Insert(new RecordatorioSalud
                {
                    IdUsuario = idUsuario,
                    Tipo = local.Tipo,
                    FrecuenciaMinutos = local.FrecuenciaMinutos,
                    FrecuenciaValor = local.FrecuenciaValor,
                    FrecuenciaUnidad = local.FrecuenciaUnidad,
                    Activo = local.Activo
                });
                var remoto = creado.Models.FirstOrDefault();
                if (remoto is not null)
                    await LocalDatabase.MarcarRecordatorioSincronizadoAsync(local.IdLocal, remoto.Id);
            }
        }

        var eliminados = await LocalDatabase.RecordatoriosPorSyncStateAsync(idUsuario, SyncStatus.Eliminado);
        foreach (var local in eliminados)
        {
            if (local.ServerId is int serverId)
            {
                try { await client.From<RecordatorioSalud>().Where(r => r.Id == serverId).Delete(); } catch { }
            }
            await LocalDatabase.BorrarRecordatorioLocalAsync(local.IdLocal);
        }

        await TraerRecordatoriosAsync(client, idUsuario);
    }

    private static async Task TraerRecordatoriosAsync(Client client, int idUsuario)
    {
        var remotos = await client.From<RecordatorioSalud>().Where(r => r.IdUsuario == idUsuario).Get();
        var locales = await LocalDatabase.RecordatoriosPorSyncStateAsync(idUsuario, SyncStatus.Sincronizada);

        foreach (var r in remotos.Models)
        {
            var local = locales.FirstOrDefault(l => l.ServerId == r.Id);
            if (local is null)
            {
                var db = await LocalDatabase.GetConexionAsync();
                await db.InsertAsync(new RecordatorioSaludOffline
                {
                    ServerId = r.Id,
                    IdUsuario = idUsuario,
                    Tipo = r.Tipo,
                    FrecuenciaMinutos = r.FrecuenciaMinutos,
                    FrecuenciaValor = r.FrecuenciaValor,
                    FrecuenciaUnidad = r.FrecuenciaUnidad,
                    Activo = r.Activo,
                    SyncState = SyncStatus.Sincronizada,
                    Modificado = DateTime.UtcNow
                });
            }
            else
            {
                await LocalDatabase.ActualizarRecordatorioDesdeServidorAsync(new RecordatorioSaludOffline
                {
                    ServerId = r.Id,
                    FrecuenciaMinutos = r.FrecuenciaMinutos,
                    FrecuenciaValor = r.FrecuenciaValor,
                    FrecuenciaUnidad = r.FrecuenciaUnidad,
                    Activo = r.Activo
                });
            }
        }

        var idsRemotos = remotos.Models.Select(r => r.Id).ToHashSet();
        foreach (var local in locales)
        {
            if (local.ServerId is int serverId && !idsRemotos.Contains(serverId))
                await LocalDatabase.BorrarRecordatorioLocalAsync(local.IdLocal);
        }
    }
}