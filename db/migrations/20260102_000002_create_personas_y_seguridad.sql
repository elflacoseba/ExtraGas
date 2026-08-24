-- =============================================================================
-- 20260102_000002_create_personas_y_seguridad.sql
-- Crea las tablas de personas: usuarios, empleados, clientes, contactos, proveedores.
-- =============================================================================

USE extragas;

-- -----------------------------------------------------------------------------
-- usuarios: credenciales del sistema
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS usuarios (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  username VARCHAR(50) NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  email VARCHAR(150) NULL,
  rol_id BIGINT UNSIGNED NOT NULL,
  activo BOOLEAN NOT NULL DEFAULT TRUE,
  ultimo_login DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT uq_usuarios_username UNIQUE (username),
  CONSTRAINT fk_usuarios_rol FOREIGN KEY (rol_id) REFERENCES roles(id),
  CONSTRAINT fk_usuarios_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_usuarios_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  INDEX idx_usuarios_rol (rol_id),
  INDEX idx_usuarios_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- empleados: personas que trabajan en la empresa (incluye al dueño)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS empleados (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  nombre VARCHAR(100) NOT NULL,
  apellido VARCHAR(100) NOT NULL,
  dni VARCHAR(15) NULL,
  cuil VARCHAR(15) NULL,
  telefono VARCHAR(25) NULL,
  email VARCHAR(150) NULL,
  calle VARCHAR(150) NULL,
  numero VARCHAR(10) NULL,
  piso VARCHAR(10) NULL,
  depto VARCHAR(10) NULL,
  ciudad VARCHAR(100) NULL,
  codigo_postal VARCHAR(10) NULL,
  provincia_id BIGINT UNSIGNED NULL,
  fecha_ingreso DATE NULL,
  usuario_id BIGINT UNSIGNED NULL,
  activo BOOLEAN NOT NULL DEFAULT TRUE,
  observaciones TEXT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT uq_empleados_dni UNIQUE (dni),
  CONSTRAINT fk_empleados_provincia FOREIGN KEY (provincia_id) REFERENCES provincias(id),
  CONSTRAINT fk_empleados_usuario FOREIGN KEY (usuario_id) REFERENCES usuarios(id),
  CONSTRAINT fk_empleados_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_empleados_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  INDEX idx_empleados_apellido (apellido, nombre),
  INDEX idx_empleados_usuario (usuario_id),
  INDEX idx_empleados_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- clientes: compradores de la empresa
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS clientes (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  codigo VARCHAR(20) NULL,
  nombre VARCHAR(100) NOT NULL,
  apellido VARCHAR(100) NOT NULL,
  dni VARCHAR(15) NULL,
  cuit_cuil VARCHAR(15) NULL,
  telefono_principal VARCHAR(25) NOT NULL,
  telefono_secundario VARCHAR(25) NULL,
  email VARCHAR(150) NULL,
  calle VARCHAR(150) NULL,
  numero VARCHAR(10) NULL,
  piso VARCHAR(10) NULL,
  depto VARCHAR(10) NULL,
  ciudad VARCHAR(100) NULL,
  codigo_postal VARCHAR(10) NULL,
  provincia_id BIGINT UNSIGNED NULL,
  referencias TEXT NULL,
  observaciones TEXT NULL,
  fecha_alta DATE NOT NULL,
  activo BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT fk_clientes_provincia FOREIGN KEY (provincia_id) REFERENCES provincias(id),
  CONSTRAINT fk_clientes_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_clientes_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  INDEX idx_clientes_apellido (apellido, nombre),
  INDEX idx_clientes_telefono (telefono_principal),
  INDEX idx_clientes_dni (dni),
  INDEX idx_clientes_codigo (codigo),
  INDEX idx_clientes_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- cliente_contactos: medios de contacto adicionales por cliente
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cliente_contactos (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  cliente_id BIGINT UNSIGNED NOT NULL,
  tipo_contacto_id BIGINT UNSIGNED NOT NULL,
  valor VARCHAR(150) NOT NULL,
  es_principal BOOLEAN NOT NULL DEFAULT FALSE,
  observaciones VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  CONSTRAINT fk_cliente_contactos_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id) ON DELETE CASCADE,
  CONSTRAINT fk_cliente_contactos_tipo FOREIGN KEY (tipo_contacto_id) REFERENCES tipos_contacto_cliente(id),
  INDEX idx_cliente_contactos_cliente (cliente_id),
  INDEX idx_cliente_contactos_tipo (tipo_contacto_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- proveedores: quienes abastecen a la empresa
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS proveedores (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  codigo VARCHAR(20) NULL,
  razon_social VARCHAR(150) NOT NULL,
  nombre_fantasia VARCHAR(150) NULL,
  cuit VARCHAR(15) NOT NULL,
  telefono_principal VARCHAR(25) NULL,
  telefono_secundario VARCHAR(25) NULL,
  email VARCHAR(150) NULL,
  calle VARCHAR(150) NULL,
  numero VARCHAR(10) NULL,
  piso VARCHAR(10) NULL,
  depto VARCHAR(10) NULL,
  ciudad VARCHAR(100) NULL,
  codigo_postal VARCHAR(10) NULL,
  provincia_id BIGINT UNSIGNED NULL,
  referencias TEXT NULL,
  contacto_nombre VARCHAR(150) NULL,
  contacto_telefono VARCHAR(25) NULL,
  contacto_email VARCHAR(150) NULL,
  observaciones TEXT NULL,
  activo BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  created_by BIGINT UNSIGNED NULL,
  updated_by BIGINT UNSIGNED NULL,
  deleted_at DATETIME NULL,
  CONSTRAINT uq_proveedores_cuit UNIQUE (cuit),
  CONSTRAINT chk_proveedores_cuit CHECK (cuit REGEXP '^[0-9]{11}$'),
  CONSTRAINT fk_proveedores_provincia FOREIGN KEY (provincia_id) REFERENCES provincias(id),
  CONSTRAINT fk_proveedores_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
  CONSTRAINT fk_proveedores_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
  INDEX idx_proveedores_razon_social (razon_social),
  INDEX idx_proveedores_codigo (codigo),
  INDEX idx_proveedores_deleted_at (deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SELECT 'Personas y seguridad creadas' AS status;
