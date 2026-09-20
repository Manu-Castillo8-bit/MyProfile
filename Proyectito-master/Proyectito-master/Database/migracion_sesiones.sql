-- ============================================================
-- Migración: enlazar usuarios existentes de la tabla public.usuario
-- con su cuenta en auth.users (campo auth_user_id).
--
-- Cómo usar: supabase.com → SQL Editor → copiar y ejecutar.
-- Es idempotente: solo toca filas que aún no tienen auth_user_id.
-- ============================================================

-- 1) Backfill del enlace (debe correr siempre, sin riesgo de duplicar).
UPDATE public.usuario u
SET auth_user_id = au.id
FROM auth.users au
WHERE au.email = u.correo
  AND u.auth_user_id IS NULL;

-- 2) Diagnóstico: resultados esperados tras el backfill.
--    a) Cada fila debe tener auth_user_id lleno.
SELECT id_usuario, nombre, correo, auth_user_id
FROM public.usuario
ORDER BY id_usuario;

--    b) No debe haber correos repetidos (si los hay, resolverlos a mano).
SELECT correo, COUNT(*) AS duplicados
FROM public.usuario
GROUP BY correo
HAVING COUNT(*) > 1;

--    c) Usuarios de la tabla sin cuenta de Auth enlazada (0 filas si todo cuadra).
SELECT u.id_usuario, u.correo
FROM public.usuario u
WHERE u.auth_user_id IS NULL
  AND NOT EXISTS (
        SELECT 1 FROM auth.users au WHERE au.email = u.correo
      );

--    d) Cuentas de Auth sin fila en la tabla usuario:
--       la app las crea automáticamente en el primer login exitoso.
SELECT au.id, au.email
FROM auth.users au
WHERE NOT EXISTS (
        SELECT 1 FROM public.usuario u WHERE u.auth_user_id = au.id
      );