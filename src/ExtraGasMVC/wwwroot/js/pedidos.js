// Pedidos/Edit — interacciones de UI para edición de pedidos (issue #168).
// Extraído desde Views/Pedidos/Edit.cshtml para mejorar mantenibilidad,
// permitir cache de navegador y testabilidad.
//
// Datos del servidor expuestos por la vista como window.__PEDIDOS_*:
//   - window.__PEDIDOS_ITEMS_EXISTENTES__: [{ productoId, productoNombre, tipoLinea }, ...]
//   - window.__PEDIDOS_SUBTOTAL__: número decimal con el subtotal del pedido

(function () {
    'use strict';

    var TIPO_LABELS = {
        VENTA: 'Venta',
        ENTREGA: 'Entrega',
        DEVOLUCION: 'Devolución'
    };

    function sanitizeActionUrl(action) {
        if (typeof action !== 'string') return null;
        var raw = action.trim();
        if (!raw) return null;

        try {
            var parsed = new URL(raw, window.location.origin);
            if (parsed.origin !== window.location.origin) return null;
            return parsed.href;
        } catch (e) {
            return null;
        }
    }

    /// Construye un <form> POST oculto con antiforgery + un set de campos
    /// hidden provistos como { nombre: valor }, y lo dispara. Usado por todos
    /// los handlers de transición de estado / eliminación para evitar repetir
    /// la misma lógica en cada listener.
    function submitPost(action, campos) {
        var safeAction = sanitizeActionUrl(action);
        if (!safeAction) {
            console.warn('submitPost bloqueado: data-action inválido o fuera de origen.', action);
            return;
        }

        var form = document.createElement('form');
        form.method = 'post';
        form.action = safeAction;

        var tokenSrc = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tokenSrc) {
            var token = document.createElement('input');
            token.type = 'hidden';
            token.name = '__RequestVerificationToken';
            token.value = tokenSrc.value;
            form.appendChild(token);
        }

        Object.keys(campos || {}).forEach(function (k) {
            var input = document.createElement('input');
            input.type = 'hidden';
            input.name = k;
            input.value = campos[k] == null ? '' : String(campos[k]);
            form.appendChild(input);
        });

        document.body.appendChild(form);
        form.submit();
    }

    /// Helper compartido: arma el form POST a CambiarEstado y lo envía.
    /// Si codigosPorItem es null, omite el campo codigosGarrafaJson (caso legacy).
    function enviarCambioEstado(btn, codigosPorItem) {
        Swal.fire({
            title: '¿Pasar a Confirmado?',
            text: 'El pedido cambiará a estado Confirmado.',
            icon: 'question',
            showCancelButton: true,
            confirmButtonColor: '#0d6efd',
            cancelButtonColor: '#6c757d',
            confirmButtonText: 'Sí, confirmar',
            cancelButtonText: 'Cancelar',
            reverseButtons: true
        }).then(function (result) {
            if (!result.isConfirmed) return;
            var campos = {
                id: btn.dataset.pedidoId,
                nuevoEstadoId: btn.dataset.nuevoEstadoId
            };
            if (codigosPorItem) {
                campos.codigosGarrafaJson = JSON.stringify(codigosPorItem);
            }
            submitPost(btn.dataset.action, campos);
        });
    }

    // ============================================================
    // 1) Validación de duplicados al agregar item
    // ============================================================
    (function initAddItem() {
        var form = document.getElementById('js-add-item-form');
        if (!form) return;

        var existentes = Array.isArray(window.__PEDIDOS_ITEMS_EXISTENTES__)
            ? window.__PEDIDOS_ITEMS_EXISTENTES__
            : [];

        form.addEventListener('submit', function (e) {
            var productoId = form.querySelector('[name="ProductoId"]').value;
            var tipoLinea = form.querySelector('[name="TipoLinea"]').value;
            if (!productoId || !tipoLinea) return;

            var select = form.querySelector('[name="ProductoId"]');
            var productoNombre = select.options[select.selectedIndex].text.split(' - ')[0];

            var duplicado = existentes.find(function (it) {
                return String(it.productoId) === String(productoId) && it.tipoLinea === tipoLinea;
            });

            if (duplicado) {
                e.preventDefault();
                Swal.fire({
                    icon: 'warning',
                    title: 'Producto ya agregado',
                    html: 'El producto <strong>' + productoNombre + '</strong> ya está cargado en este pedido como <strong>' + TIPO_LABELS[tipoLinea] + '</strong>.<br><br>Si necesita modificar la cantidad, primero elimine el item existente y vuelva a cargarlo.',
                    confirmButtonText: 'Entendido',
                    confirmButtonColor: '#0d6efd',
                    allowOutsideClick: true,
                    allowEscapeKey: true
                });
            }
        });
    })();

    // ============================================================
    // 2) Confirmación SweetAlert para eliminar item
    // ============================================================
    document.querySelectorAll('.js-remove-item-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var itemId = btn.dataset.itemId;
            var pedidoId = btn.dataset.pedidoId;
            var producto = btn.dataset.producto || 'este item';
            var action = btn.dataset.action;
            Swal.fire({
                title: '¿Eliminar item?',
                html: 'Se eliminará <strong>' + producto + '</strong> del pedido. Esta acción no se puede deshacer.',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonColor: '#dc3545',
                cancelButtonColor: '#6c757d',
                confirmButtonText: 'Sí, eliminar',
                cancelButtonText: 'Cancelar',
                reverseButtons: true
            }).then(function (result) {
                if (result.isConfirmed) {
                    submitPost(action, {
                        itemId: itemId,
                        pedidoId: pedidoId
                    });
                }
            });
        });
    });

    // ============================================================
    // 3) Confirmación para pasar a ENTREGADO
    // ============================================================
    document.querySelectorAll('.js-entregar-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
            Swal.fire({
                title: '¿Confirmar entrega?',
                html: 'El pedido pasará a estado <strong>Entregado</strong>.<br>Esta acción <strong>no se puede deshacer</strong>.',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonColor: '#228B22',
                cancelButtonColor: '#6c757d',
                confirmButtonText: 'Sí, marcar como entregado',
                cancelButtonText: 'Volver',
                reverseButtons: true
            }).then(function (result) {
                if (result.isConfirmed) {
                    submitPost(btn.dataset.action, {
                        id: btn.dataset.pedidoId,
                        nuevoEstadoId: btn.dataset.nuevoEstadoId
                    });
                }
            });
        });
    });

    // ============================================================
    // 4) Confirmación para CANCELADO (con motivo)
    // ============================================================
    document.querySelectorAll('.js-cancelar-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
            Swal.fire({
                title: 'Cancelar Pedido',
                html: '<p class="text-danger mb-2">Esta acción <strong>no se puede deshacer</strong>.</p>',
                input: 'textarea',
                inputLabel: 'Motivo de cancelación',
                inputPlaceholder: 'Describa el motivo de la cancelación...',
                inputAttributes: { maxlength: 500 },
                showCancelButton: true,
                confirmButtonColor: '#dc3545',
                cancelButtonColor: '#6c757d',
                confirmButtonText: 'Sí, cancelar pedido',
                cancelButtonText: 'Volver',
                reverseButtons: true,
                inputValidator: function (value) {
                    if (!value || !value.trim()) {
                        return 'Debe ingresar un motivo de cancelación';
                    }
                    if (value.length > 500) {
                        return 'Máximo 500 caracteres';
                    }
                }
            }).then(function (result) {
                if (result.isConfirmed) {
                    submitPost(btn.dataset.action, {
                        id: btn.dataset.pedidoId,
                        nuevoEstadoId: btn.dataset.nuevoEstadoId,
                        motivoCancelacion: result.value.trim()
                    });
                }
            });
        });
    });

    // ============================================================
    // 5) Confirmación para EN_PREPARACION / PENDIENTE
    // ============================================================
    document.querySelectorAll('.js-preparacion-btn').forEach(function (btn) {
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            var estadoNombre = btn.dataset.estadoNombre;
            Swal.fire({
                title: '¿Cambiar estado?',
                html: 'El pedido pasará a estado <strong>' + estadoNombre + '</strong>.',
                icon: 'question',
                showCancelButton: true,
                confirmButtonColor: '#0d6efd',
                cancelButtonColor: '#6c757d',
                confirmButtonText: 'Sí, continuar',
                cancelButtonText: 'Cancelar',
                reverseButtons: true
            }).then(function (result) {
                if (result.isConfirmed) {
                    submitPost(btn.dataset.action, {
                        id: btn.dataset.pedidoId,
                        nuevoEstadoId: btn.dataset.nuevoEstadoId
                    });
                }
            });
        });
    });

    // ============================================================
    // 6) Confirmación para CONFIRMADO (con modal de canje si hay items GARRAFA)
    // ============================================================
    document.querySelectorAll('.js-confirmar-btn').forEach(function (btn) {
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            var tieneItems = btn.dataset.tieneItems === 'true';
            var tieneItemsCanje = btn.dataset.tieneItemsCanje === 'true';

            if (!tieneItems) {
                Swal.fire({
                    icon: 'warning',
                    title: 'No se puede confirmar',
                    html: 'El pedido no tiene <strong>ningún ítem</strong> agregado.<br>Agregue al menos un ítem antes de pasarlo a <strong>Confirmado</strong>.',
                    confirmButtonText: 'Entendido',
                    confirmButtonColor: '#0d6efd',
                    allowOutsideClick: true,
                    allowEscapeKey: true
                });
                return;
            }

            // Si hay items GARRAFA-capaces con ENTREGA/DEVOLUCION, abrimos
            // el modal de carga de códigos antes de confirmar. El modal
            // hace su propia validación y, al confirmar, serializa los
            // códigos a JSON y dispara el submit.
            if (tieneItemsCanje) {
                var modalEl = document.getElementById('js-canje-garrafas-modal');
                if (modalEl && window.bootstrap) {
                    // Limpiar textareas de intentos previos antes de mostrar.
                    modalEl.querySelectorAll('.js-canje-textarea').forEach(function (ta) {
                        ta.value = '';
                        ta.classList.remove('is-invalid');
                    });
                    var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
                    modal.show();
                } else {
                    // Fallback defensivo: si el modal no existe por algún
                    // motivo, seguimos el flujo legacy.
                    enviarCambioEstado(btn, null);
                }
                return;
            }

            // Pedido solo con items no-canje (VENTA / carbón / leña): flujo directo.
            enviarCambioEstado(btn, null);
        });
    });

    // ============================================================
    // 7) Modal de canje — validación de códigos y submit con JSON
    // ============================================================
    (function initCanjeModal() {
        var modalConfirmar = document.querySelector('.js-canje-confirmar');
        if (!modalConfirmar) return;

        modalConfirmar.addEventListener('click', function () {
            var textareas = document.querySelectorAll('.js-canje-textarea');
            var invalidos = [];
            var codigosPorItem = {};

            textareas.forEach(function (ta) {
                var itemId = ta.dataset.itemId;
                var esperada = Number.parseInt(ta.dataset.esperada, 10);
                var codigos = (ta.value || '')
                    .split(/\r?\n/)
                    .map(function (s) { return s.trim(); })
                    .filter(function (s) { return s.length > 0; });

                // Dedupe preservando orden, case-sensitive (los códigos son literales).
                var seen = {};
                var unicos = [];
                codigos.forEach(function (c) {
                    if (!seen[c]) { seen[c] = true; unicos.push(c); }
                });

                if (unicos.length !== esperada) {
                    ta.classList.add('is-invalid');
                    invalidos.push({
                        itemId: itemId,
                        esperada: esperada,
                        recibida: unicos.length
                    });
                } else {
                    ta.classList.remove('is-invalid');
                    codigosPorItem[itemId] = unicos;
                }
            });

            if (invalidos.length > 0) {
                var detalle = invalidos.map(function (i) {
                    return 'Item ' + i.itemId + ': esperaba ' + i.esperada + ', recibí ' + i.recibida + '.';
                }).join('<br>');
                Swal.fire({
                    icon: 'warning',
                    title: 'Códigos incompletos',
                    html: detalle,
                    confirmButtonText: 'Revisar',
                    confirmButtonColor: '#0d6efd',
                    allowOutsideClick: true,
                    allowEscapeKey: true
                });
                return;
            }

            // Serializar a JSON y cerrar modal. El submit lo dispara el
            // botón Confirmar (con SweetAlert) desde el handler de la card.
            var modalEl = document.getElementById('js-canje-garrafas-modal');
            window.bootstrap?.Modal?.getOrCreateInstance(modalEl)?.hide();

            // Disparar el submit. Usamos el primer botón confirmar de la
            // página (solo hay uno en Edit.cshtml).
            var btn = document.querySelector('.js-confirmar-btn');
            if (btn) {
                enviarCambioEstado(btn, codigosPorItem);
            }
        });

        // Limpiar el feedback de invalid al editar.
        document.querySelectorAll('.js-canje-textarea').forEach(function (ta) {
            ta.addEventListener('input', function () {
                ta.classList.remove('is-invalid');
            });
        });
    })();

    // ============================================================
    // 8) Auto-scroll a la tabla de items
    // ============================================================
    (function initAutoScroll() {
        var target = document.getElementById('itemsTable');
        if (!target) return;
        var offset = 70;
        var rect = target.getBoundingClientRect();
        if (rect.top < offset || rect.top > window.innerHeight) {
            var top = window.pageYOffset + rect.top - offset;
            window.scrollTo({ top: top, behavior: 'smooth' });
        }
    })();

    // ============================================================
    // 9) Recálculo de descuento → total en cliente
    // ============================================================
    (function initRecalcTotal() {
        var descuento = document.getElementById('js-descuento');
        var totalDisplay = document.getElementById('js-total');
        var subtotalValue = Number(window.__PEDIDOS_SUBTOTAL__);
        if (!descuento || !totalDisplay || Number.isNaN(subtotalValue)) return;

        function recalcularTotal() {
            var d = Number.parseFloat(descuento.value) || 0;
            if (d < 0) d = 0;
            if (d > 100) d = 100;
            var result = subtotalValue - (subtotalValue * d / 100);
            totalDisplay.value = result.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        descuento.addEventListener('blur', recalcularTotal);
        descuento.addEventListener('input', function () {
            var v = Number.parseFloat(descuento.value);
            if (!Number.isNaN(v) && v > 100) descuento.value = 100;
            if (!Number.isNaN(v) && v < 0) descuento.value = 0;
        });
    })();
})();
