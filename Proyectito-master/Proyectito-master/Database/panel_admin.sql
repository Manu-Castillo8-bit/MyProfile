-- ============================================================
-- PANEL DE ADMINISTRADOR (Supabase)
-- Cómo usar: supabase.com → SQL Editor → copiar y ejecutar TODO el bloque.
-- Es seguro repetirlo (idempotente).
--
-- Qué hace:
--   1) Agrega la columna "rol" a la tabla public.usuario ('usuario'/'admin').
--   2) Función es_admin(): dice si el usuario autenticado es admin.
--   3) Funciones RPC para el panel (solo accesibles si quien llama es admin):
--        - admin_listar_usuarios()               -> jsonb con todos los usuarios
--        - admin_datos_usuario(p_usuario)        -> jsonb con tareas/movimientos/
--                                                   contraseñas/recordatorios
--        - CRUD: admin_actualizar_usuario / admin_(insertar|actualizar|eliminar)_
--                _tarea / _movimiento / _contrasena / _recordatorio
--        - admin_eliminar_usuario(p_usuario)     -> datos + fila + cuenta Auth
--   4) GRANT para que la app (anon key) pueda invocarlas.
--
-- IMPORTANTE: cambia el correo del paso 2 para convertir al primer admin.
-- ============================================================

-- 1) COLUMNA DE ROL ---------------------------------------------------------
alter table public.usuario add column if not exists rol text not null default 'usuario';

-- 2) PRIMER ADMIN (reemplaza el correo) -------------------------------------
update public.usuario set rol = 'admin' where correo = 'TUCORREO@EJEMPLO.COM';

-- 3) FUNCIÓN es_admin() ------------------------------------------------------
-- Comprueba si el usuario autenticado es admin. Compara por auth_user_id y
-- también por el correo del JWT (funciona aunque una cuenta no tenga el
-- auth_user_id enlazado en la tabla usuario).
create or replace function public.es_admin()
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1
    from public.usuario u
    where u.rol = 'admin'
      and (u.auth_user_id = auth.uid()
           or lower(coalesce(u.correo, '')) = lower(coalesce(auth.jwt() ->> 'email', '')))
  );
$$;

-- 4) LISTAR USUARIOS ----------------------------------------------------------
create or replace function public.admin_listar_usuarios()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare v_resultado jsonb;
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;

  select coalesce(jsonb_agg(jsonb_build_object(
    'id',     u.id_usuario,
    'nombre', u.nombre,
    'correo', u.correo,
    'rol',    u.rol
  ) order by u.id_usuario), '[]'::jsonb)
  into v_resultado
  from public.usuario u;

  return v_resultado;
end;
$$;

-- 5) VER DATOS DE UN USUARIO ---------------------------------------------------
create or replace function public.admin_datos_usuario(p_usuario integer)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare v_resultado jsonb;
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;

  if not exists (select 1 from public.usuario where id_usuario = p_usuario) then
    raise exception 'El usuario no existe';
  end if;

  select jsonb_build_object(
    'usuario', (
      select jsonb_build_object(
        'id',     u.id_usuario,
        'nombre', u.nombre,
        'correo', u.correo,
        'rol',    u.rol
      )
      from public.usuario u where u.id_usuario = p_usuario
    ),
    'tareas', coalesce((
      select jsonb_agg(jsonb_build_object(
        'id', t.id_tarea, 'titulo', t.titulo, 'descripcion', t.descripcion,
        'fecha_vencimiento', t.fecha_vencimiento, 'estado', t.estado
      )) from public.tarea t where t.id_usuario = p_usuario
    ), '[]'::jsonb),
    'movimientos', coalesce((
      select jsonb_agg(jsonb_build_object(
        'id', m.id_movimiento, 'monto', m.monto, 'tipo', m.tipo,
        'descripcion', m.descripcion, 'fecha', m.fecha
      )) from public.movimiento_financiero m where m.id_usuario = p_usuario
    ), '[]'::jsonb),
    'contrasenas', coalesce((
      select jsonb_agg(jsonb_build_object(
        'id', c.id_contrasena, 'sitio', c.sitio_web, 'usuario', c.usuario_cuenta,
        'clave_cifrada', c.clave_cifrada
      )) from public.contrasenas c where c.id_usuario = p_usuario
    ), '[]'::jsonb),
    'recordatorios', coalesce((
      select jsonb_agg(jsonb_build_object(
        'id', r.id_recordatorio, 'tipo', r.tipo_recordatorio,
        'frecuencia_minutos', r.frecuencia_minutos, 'activo', r.activo
      )) from public.recordatorio_salud r where r.id_usuario = p_usuario
    ), '[]'::jsonb)
  )
  into v_resultado;

  return v_resultado;
end;
$$;

-- 6) ELIMINAR REGISTROS SUELTOS DE UN USUARIO (verifica dueño) ------------------
create or replace function public.admin_eliminar_tarea(p_usuario integer, p_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  delete from public.tarea where id_tarea = p_id and id_usuario = p_usuario;
end;
$$;

create or replace function public.admin_eliminar_movimiento(p_usuario integer, p_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  delete from public.movimiento_financiero where id_movimiento = p_id and id_usuario = p_usuario;
end;
$$;

create or replace function public.admin_eliminar_contrasena(p_usuario integer, p_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  delete from public.contrasenas where id_contrasena = p_id and id_usuario = p_usuario;
end;
$$;

create or replace function public.admin_eliminar_recordatorio(p_usuario integer, p_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  delete from public.recordatorio_salud where id_recordatorio = p_id and id_usuario = p_usuario;
end;
$$;

-- 7) ELIMINAR USUARIO COMPLETO (datos + fila + cuenta de Auth) -------------------
create or replace function public.admin_eliminar_usuario(p_usuario integer)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare v_auth uuid;
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;

  select auth_user_id into v_auth from public.usuario where id_usuario = p_usuario;

  delete from public.recordatorio_salud where id_usuario = p_usuario;
  delete from public.tarea where id_usuario = p_usuario;
  delete from public.movimiento_financiero where id_usuario = p_usuario;
  delete from public.contrasenas where id_usuario = p_usuario;
  delete from public.usuario where id_usuario = p_usuario;

  if v_auth is not null then
    delete from auth.users where id = v_auth;
  end if;
end;
$$;

-- 8) PERMISOS: la app (anon key / authenticated) Solo puede llamar a estas ---------
grant execute on function public.es_admin to anon, authenticated;
grant execute on function public.admin_listar_usuarios to anon, authenticated;
grant execute on function public.admin_datos_usuario(integer) to anon, authenticated;
grant execute on function public.admin_eliminar_tarea(integer, bigint) to anon, authenticated;
grant execute on function public.admin_eliminar_movimiento(integer, bigint) to anon, authenticated;
grant execute on function public.admin_eliminar_contrasena(integer, bigint) to anon, authenticated;
grant execute on function public.admin_eliminar_recordatorio(integer, bigint) to anon, authenticated;
grant execute on function public.admin_eliminar_usuario(integer) to anon, authenticated;

-- 9) CRUD: EDITAR DATOS DEL USUARIO ---------------------------------------------
-- Verifica que quien llama es admin. Un admin no puede quitarse su propio rol.
create or replace function public.admin_actualizar_usuario(
  p_usuario integer, p_nombre text default null, p_rol text default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;

  if p_rol is not null and p_rol not in ('admin', 'usuario') then
    raise exception 'Rol inválido';
  end if;

  if p_rol = 'usuario' and exists (
    select 1 from public.usuario
    where id_usuario = p_usuario
      and (auth_user_id = auth.uid()
           or lower(coalesce(correo, '')) = lower(coalesce(auth.jwt() ->> 'email', '')))
  ) then
    raise exception 'No puedes quitar tu propio rol de administrador';
  end if;

  update public.usuario
  set nombre = coalesce(p_nombre, nombre),
      rol    = coalesce(p_rol, rol)
  where id_usuario = p_usuario;
end;
$$;

-- 10) CRUD: TAREAS ----------------------------------------------------------------
create or replace function public.admin_insertar_tarea(
  p_usuario integer, p_titulo text, p_descripcion text default null,
  p_fecha_vencimiento timestamptz default null, p_estado text default 'Pendiente')
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  if coalesce(p_titulo, '') = '' then
    raise exception 'El título es obligatorio';
  end if;
  insert into public.tarea (id_usuario, titulo, descripcion, fecha_vencimiento, estado)
  values (p_usuario, p_titulo, coalesce(p_descripcion, ''), p_fecha_vencimiento, p_estado);
end;
$$;

create or replace function public.admin_actualizar_tarea(
  p_usuario integer, p_tarea bigint, p_titulo text default null,
  p_descripcion text default null, p_fecha_vencimiento timestamptz default null,
  p_estado text default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  update public.tarea
  set titulo = coalesce(p_titulo, titulo),
      descripcion = case when p_descripcion is not null then p_descripcion else descripcion end,
      fecha_vencimiento = coalesce(p_fecha_vencimiento, fecha_vencimiento),
      estado = coalesce(p_estado, estado)
  where id_tarea = p_tarea and id_usuario = p_usuario;
end;
$$;

-- 11) CRUD: MOVIMIENTOS (ahorro) ---------------------------------------------------
create or replace function public.admin_insertar_movimiento(
  p_usuario integer, p_monto numeric, p_tipo text,
  p_descripcion text default null, p_fecha timestamptz default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  if p_tipo not in ('ingreso', 'gasto') then
    raise exception 'Tipo de movimiento inválido';
  end if;
  if p_monto is null or p_monto <= 0 then
    raise exception 'El monto debe ser mayor a cero';
  end if;
  insert into public.movimiento_financiero (id_usuario, monto, tipo, descripcion, fecha)
  values (p_usuario, p_monto, p_tipo, coalesce(p_descripcion, ''), coalesce(p_fecha, now()));
end;
$$;

create or replace function public.admin_actualizar_movimiento(
  p_usuario integer, p_movimiento bigint, p_monto numeric default null,
  p_tipo text default null, p_descripcion text default null, p_fecha timestamptz default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  if p_tipo is not null and p_tipo not in ('ingreso', 'gasto') then
    raise exception 'Tipo de movimiento inválido';
  end if;
  update public.movimiento_financiero
  set monto = coalesce(p_monto, monto),
      tipo = coalesce(p_tipo, tipo),
      descripcion = case when p_descripcion is not null then p_descripcion else descripcion end,
      fecha = coalesce(p_fecha, fecha)
  where id_movimiento = p_movimiento and id_usuario = p_usuario;
end;
$$;

-- 12) CRUD: CONTRASEÑAS -------------------------------------------------------------
create or replace function public.admin_insertar_contrasena(
  p_usuario integer, p_sitio text, p_usuario_cuenta text default null, p_clave text default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  if coalesce(p_sitio, '') = '' then
    raise exception 'El sitio es obligatorio';
  end if;
  insert into public.contrasenas (id_usuario, sitio_web, usuario_cuenta, clave_cifrada)
  values (p_usuario, p_sitio, coalesce(p_usuario_cuenta, ''), coalesce(p_clave, ''));
end;
$$;

create or replace function public.admin_actualizar_contrasena(
  p_usuario integer, p_contrasena bigint, p_sitio text default null,
  p_usuario_cuenta text default null, p_clave text default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  update public.contrasenas
  set sitio_web = coalesce(p_sitio, sitio_web),
      usuario_cuenta = case when p_usuario_cuenta is not null then p_usuario_cuenta else usuario_cuenta end,
      clave_cifrada = case when p_clave is not null then p_clave else clave_cifrada end
  where id_contrasena = p_contrasena and id_usuario = p_usuario;
end;
$$;

-- 13) CRUD: RECORDATORIOS (salud) ----------------------------------------------------
create or replace function public.admin_insertar_recordatorio(
  p_usuario integer, p_tipo text, p_frecuencia_minutos integer default 45, p_activo boolean default true)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  if p_tipo not in ('agua', 'descanso', 'tareas') then
    raise exception 'Tipo de recordatorio inválido';
  end if;
  if p_frecuencia_minutos is null or p_frecuencia_minutos <= 0 then
    raise exception 'La frecuencia debe ser mayor a cero';
  end if;
  insert into public.recordatorio_salud (id_usuario, tipo_recordatorio, frecuencia_minutos, activo)
  values (p_usuario, p_tipo, p_frecuencia_minutos, p_activo)
  on conflict (id_usuario, tipo_recordatorio)
  do update set frecuencia_minutos = excluded.frecuencia_minutos, activo = excluded.activo;
end;
$$;

create or replace function public.admin_actualizar_recordatorio(
  p_usuario integer, p_recordatorio bigint, p_tipo text default null,
  p_frecuencia_minutos integer default null, p_activo boolean default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;
  if p_tipo is not null and p_tipo not in ('agua', 'descanso', 'tareas') then
    raise exception 'Tipo de recordatorio inválido';
  end if;
  update public.recordatorio_salud
  set tipo_recordatorio = coalesce(p_tipo, tipo_recordatorio),
      frecuencia_minutos = coalesce(p_frecuencia_minutos, frecuencia_minutos),
      activo = coalesce(p_activo, activo)
  where id_recordatorio = p_recordatorio and id_usuario = p_usuario;
end;
$$;

-- 14) PROTECCIÓN: el admin no puede eliminarse a sí mismo ------------------------------
create or replace function public.admin_eliminar_usuario(p_usuario integer)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare v_auth uuid;
begin
  if not public.es_admin() then
    raise exception 'No autorizado';
  end if;

  if exists (
    select 1 from public.usuario
    where id_usuario = p_usuario
      and (auth_user_id = auth.uid()
           or lower(coalesce(correo, '')) = lower(coalesce(auth.jwt() ->> 'email', '')))
  ) then
    raise exception 'No puedes eliminarte a ti mismo';
  end if;

  select auth_user_id into v_auth from public.usuario where id_usuario = p_usuario;

  delete from public.recordatorio_salud where id_usuario = p_usuario;
  delete from public.tarea where id_usuario = p_usuario;
  delete from public.movimiento_financiero where id_usuario = p_usuario;
  delete from public.contrasenas where id_usuario = p_usuario;
  delete from public.usuario where id_usuario = p_usuario;

  if v_auth is not null then
    delete from auth.users where id = v_auth;
  end if;
end;
$$;

-- 15) PERMISOS DE LAS FUNCIONES CRUD NUEVAS ----------------------------------------------
grant execute on function public.admin_actualizar_usuario(integer, text, text) to anon, authenticated;
grant execute on function public.admin_insertar_tarea(integer, text, text, timestamptz, text) to anon, authenticated;
grant execute on function public.admin_actualizar_tarea(integer, bigint, text, text, timestamptz, text) to anon, authenticated;
grant execute on function public.admin_insertar_movimiento(integer, numeric, text, text, timestamptz) to anon, authenticated;
grant execute on function public.admin_actualizar_movimiento(integer, bigint, numeric, text, text, timestamptz) to anon, authenticated;
grant execute on function public.admin_insertar_contrasena(integer, text, text, text) to anon, authenticated;
grant execute on function public.admin_actualizar_contrasena(integer, bigint, text, text, text) to anon, authenticated;
grant execute on function public.admin_insertar_recordatorio(integer, text, integer, boolean) to anon, authenticated;
grant execute on function public.admin_actualizar_recordatorio(integer, bigint, text, integer, boolean) to anon, authenticated;

-- ============================================================
-- (OPCIONAL) Reforzar RLS en la tabla usuario.
-- OJO: la app actual depende de poder crear/leer su propia fila.
-- Esta variante permite al dueño ver/crear/editar su fila y al admin
-- ver todas. Solo actívala si compruebas que tu registro/login siguen
-- funcionando. Con el panel (SECURITY DEFINER) no es imprescindible.
-- ============================================================
-- alter table public.usuario enable row level security;
--
-- drop policy if exists "usuario_select_own" on public.usuario;
-- create policy "usuario_select_own" on public.usuario for select
--   using (auth.uid() = auth_user_id or public.es_admin());
--
-- drop policy if exists "usuario_insert_own" on public.usuario;
-- create policy "usuario_insert_own" on public.usuario for insert
--   with check (auth.uid() = auth_user_id);
--
-- drop policy if exists "usuario_update_own" on public.usuario;
-- create policy "usuario_update_own" on public.usuario for update
--   using (auth.uid() = auth_user_id or public.es_admin());