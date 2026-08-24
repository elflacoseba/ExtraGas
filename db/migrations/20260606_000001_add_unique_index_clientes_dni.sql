USE extragas;

DROP INDEX idx_clientes_dni ON clientes;
CREATE UNIQUE INDEX idx_clientes_dni ON clientes(dni);
