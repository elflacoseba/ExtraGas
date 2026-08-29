// Recepciones — items dinámicos con binding GARRAFA (issue #45, PR2 UI).
// Reescribe los items del tbody como hidden inputs `Items[i].X` y
// `Items[i].CodigosGarrafa[j]` para que el DefaultModelBinder mapee a
// CrearRecepcionDto.

(function () {
    'use strict';

    var form = document.getElementById('recepcion-form');
    if (!form) return;

    var tbody = document.getElementById('items-tbody');
    var emptyRow = document.getElementById('items-empty-row');
    var btnAdd = document.getElementById('btn-agregar-item');
    var btnConfirm = document.getElementById('btn-confirmar');
    var totalGarrafasBadge = document.querySelector('.js-total-garrafas');
    var hiddenMount = document.getElementById('js-hidden-mount');

    var productos = (Array.isArray(window.__RECEPCIONES_PRODUCTOS__)
        ? window.__RECEPCIONES_PRODUCTOS__ : []).map(function (p) {
        return {
            id: String(p.id),
            nombre: String(p.nombre || ''),
            capacidadKg: p.capacidadKg != null ? Number(p.capacidadKg) : null,
            precioActual: Number(p.precioActual) || 0,
            manejaGarrafaIndividual: !!p.manejaGarrafaIndividual,
        };
    });
    var productoById = Object.create(null);
    productos.forEach(function (p) { productoById[p.id] = p; });

    var dataPreCargada = Array.isArray(window.__RECEPCIONES_ITEMS_PREVIEW__)
        ? window.__RECEPCIONES_ITEMS_PREVIEW__ : [];

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function money(n) { return '$' + (Number(n) || 0).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
    function splitCodigos(t) { return (t || '').split(/[\r\n,;]+/).map(function (s) { return s.trim(); }).filter(Boolean); }

    function opcionesProductos() {
        var html = '<option value="">-- Seleccione producto --</option>';
        productos.forEach(function (p) {
            var cap = p.capacidadKg != null ? ' (' + p.capacidadKg + ' kg)' : '';
            html += '<option value="' + esc(p.id) + '" data-precio="' + p.precioActual + '"'
                + ' data-garrafa="' + (p.manejaGarrafaIndividual ? 1 : 0) + '">'
                + esc(p.nombre) + esc(cap) + '</option>';
        });
        return html;
    }

    function filaHtml() {
        return ''
            + '<tr data-row>'
            +   '<td><select class="form-select form-select-sm js-producto" required>' + opcionesProductos() + '</select></td>'
            +   '<td><input type="number" min="0.01" step="0.01" class="form-control form-control-sm js-cantidad" required /></td>'
            +   '<td><input type="number" min="0" step="0.01" class="form-control form-control-sm js-precio" required /></td>'
            +   '<td class="text-end js-subtotal">$0,00</td>'
            +   '<td class="js-codigos-cell d-none">'
            +     '<textarea class="form-control form-control-sm js-codigos" rows="3" placeholder="Una línea por código: G001, G002, ..."></textarea>'
            +     '<div class="form-text small js-codigos-help"></div>'
            +   '</td>'
            +   '<td class="text-end"><button type="button" class="btn btn-sm btn-outline-danger js-eliminar" title="Eliminar fila"><i class="bi bi-trash"></i></button></td>'
            + '</tr>';
    }

    function wireFila(tr) {
        var sel = tr.querySelector('.js-producto');
        var cant = tr.querySelector('.js-cantidad');
        var precio = tr.querySelector('.js-precio');
        var sub = tr.querySelector('.js-subtotal');
        var cell = tr.querySelector('.js-codigos-cell');
        var area = tr.querySelector('.js-codigos');
        var help = tr.querySelector('.js-codigos-help');

        function subtotal() { sub.textContent = money(Number(cant.value) * Number(precio.value)); }

        function onChangeProducto() {
            var p = productoById[sel.value];
            if (p) {
                if (precio.value === '' || Number(precio.value) === 0) precio.value = p.precioActual.toFixed(2);
                cell.classList.toggle('d-none', !p.manejaGarrafaIndividual);
                area.required = p.manejaGarrafaIndividual;
                if (!p.manejaGarrafaIndividual) { area.value = ''; help.textContent = ''; }
            } else {
                cell.classList.add('d-none');
            }
            subtotal();
            actualizarTotal();
        }

        function validarDuplicados() {
            var codes = splitCodigos(area.value);
            var seen = {}, dupes = [];
            codes.forEach(function (c) {
                var k = c.toLowerCase();
                if (seen[k]) dupes.push(c); else seen[k] = true;
            });
            help.textContent = dupes.length > 0 ? 'Duplicados: ' + Array.from(new Set(dupes.map(function (d) { return d.toLowerCase(); }))).join(', ') : '';
            help.classList.toggle('text-danger', dupes.length > 0);
        }

        sel.addEventListener('change', onChangeProducto);
        cant.addEventListener('input', function () { subtotal(); actualizarTotal(); });
        precio.addEventListener('input', subtotal);
        area.addEventListener('input', function () { validarDuplicados(); actualizarTotal(); });

        tr.querySelector('.js-eliminar').addEventListener('click', function () {
            tr.remove();
            if (!tbody.querySelector('tr[data-row]') && emptyRow) emptyRow.style.display = '';
            actualizarTotal();
        });
    }

    function agregarFila(prefill) {
        if (emptyRow) emptyRow.style.display = 'none';
        var wrap = document.createElement('tbody');
        wrap.innerHTML = filaHtml();
        var tr = wrap.firstElementChild;
        tbody.appendChild(tr);
        wireFila(tr);

        if (prefill) {
            tr.querySelector('.js-producto').value = String(prefill.productoId || '');
            tr.querySelector('.js-cantidad').value = prefill.cantidad != null ? Number(prefill.cantidad) : '';
            tr.querySelector('.js-precio').value = prefill.precioUnitario != null ? Number(prefill.precioUnitario) : '';
            tr.querySelector('.js-producto').dispatchEvent(new Event('change'));
            if (Array.isArray(prefill.codigosGarrafa)) tr.querySelector('.js-codigos').value = prefill.codigosGarrafa.join('\n');
            tr.querySelector('.js-cantidad').dispatchEvent(new Event('input'));
        }
        actualizarTotal();
    }

    function actualizarTotal() {
        var total = 0, conProducto = 0;
        tbody.querySelectorAll('tr[data-row]').forEach(function (tr) {
            var p = productoById[tr.querySelector('.js-producto').value];
            if (p) {
                conProducto++;
                if (p.manejaGarrafaIndividual) total += splitCodigos(tr.querySelector('.js-codigos').value).length;
            }
        });
        if (totalGarrafasBadge) totalGarrafasBadge.textContent = String(total);
        if (btnConfirm) btnConfirm.disabled = conProducto === 0;
    }

    function itemsFromForm() {
        var items = [];
        tbody.querySelectorAll('tr[data-row]').forEach(function (tr) {
            var pid = tr.querySelector('.js-producto').value;
            if (!pid) return;
            var p = productoById[pid];
            items.push({
                productoId: pid,
                productoNombre: p ? p.nombre : '',
                manejaGarrafaIndividual: !!(p && p.manejaGarrafaIndividual),
                cantidad: Number(tr.querySelector('.js-cantidad').value) || 0,
                precioUnitario: Number(tr.querySelector('.js-precio').value) || 0,
                codigosGarrafa: splitCodigos(tr.querySelector('.js-codigos').value),
            });
        });
        return items;
    }

    function validarCliente(items) {
        if (items.length === 0) return 'Debe agregar al menos un item antes de confirmar.';
        for (var i = 0; i < items.length; i++) {
            var msg = validarItem(items[i], i + 1);
            if (msg !== null) return msg;
        }
        return null;
    }

    /// Devuelve un mensaje de error si el item no cumple las reglas del
    /// formulario; null si está OK. Las reglas GARRAFA (cantidad entera,
    /// cantidad vs códigos, dedupe) se aplican solo si el producto tiene
    /// tracking individual.
    function validarItem(it, idx) {
        if (!(it.cantidad > 0)) return 'Item ' + idx + ': la cantidad debe ser mayor a 0.';
        if (it.precioUnitario < 0) return 'Item ' + idx + ': el precio unitario no puede ser negativo.';
        if (!it.manejaGarrafaIndividual) return null;
        return validarCodigosGarrafa(it, idx);
    }

    /// Valida cantidad, conteo de códigos y duplicados para items GARRAFA.
    function validarCodigosGarrafa(it, idx) {
        if (Math.trunc(it.cantidad) !== it.cantidad)
            return 'Item ' + idx + ' (' + it.productoNombre + '): la cantidad debe ser entera para GARRAFA.';

        var esperado = Math.trunc(it.cantidad);
        if (esperado !== it.codigosGarrafa.length)
            return 'Item ' + idx + ' (' + it.productoNombre + '): esperaba ' + esperado + ' código(s) y recibió ' + it.codigosGarrafa.length + '.';

        var dupes = duplicadosInsensitive(it.codigosGarrafa);
        if (dupes.length > 0)
            return 'Item ' + idx + ' (' + it.productoNombre + '): códigos duplicados: ' + dupes.join(', ') + '.';

        return null;
    }

    /// Devuelve la lista de códigos duplicados (case-insensitive) sin repetir
    /// el mismo duplicado en el resultado.
    function duplicadosInsensitive(codigos) {
        var seen = {}, dupes = [];
        codigos.forEach(function (c) {
            var k = c.toLowerCase();
            if (seen[k]) dupes.push(c); else seen[k] = true;
        });
        return Array.from(new Set(dupes));
    }

    function resumenHtml(items, totales) {
        var totG = items.filter(function (i) { return i.manejaGarrafaIndividual; }).reduce(function (a, b) { return a + b.codigosGarrafa.length; }, 0);
        var totNoG = items.filter(function (i) { return !i.manejaGarrafaIndividual; }).length;
        var html = '<p class="text-start mb-3">Items: <strong>' + items.length + '</strong>';
        if (totG > 0) html += ' · <strong>' + totG + '</strong> garrafa(s) nueva(s) en <em>Llena Depósito</em>';
        if (totNoG > 0) html += ' · <strong>' + totNoG + '</strong> item(s) sin tracking';
        html += '.</p>';
        html += '<table class="table table-sm table-bordered mb-2 text-start"><thead><tr><th>#</th><th>Producto</th><th>Cant.</th><th>Precio</th><th>Subtotal</th><th>Códigos</th></tr></thead><tbody>';
        items.forEach(function (it, idx) {
            html += '<tr><td>' + (idx + 1) + '</td><td>' + esc(it.productoNombre) + '</td>'
                + '<td>' + it.cantidad + '</td><td>' + money(it.precioUnitario) + '</td>'
                + '<td>' + money(it.cantidad * it.precioUnitario) + '</td>'
                + '<td>' + (it.manejaGarrafaIndividual ? it.codigosGarrafa.length + '<br><small class="text-muted">' + esc(it.codigosGarrafa.join(', ')) + '</small>' : '<span class="text-muted">—</span>') + '</td></tr>';
        });
        html += '</tbody></table>';
        html += '<p class="text-start mb-0 small text-muted">Subtotal: <strong>' + money(totales.subtotal) + '</strong>'
            + ' · Descuento: <strong>' + money(totales.descuento) + '</strong>'
            + ' · Total: <strong>' + money(totales.total) + '</strong></p>';
        return html;
    }

    function leerTotales() {
        return {
            subtotal: Number((document.getElementById('Recepcion_Subtotal') || {}).value) || 0,
            descuento: Number((document.getElementById('Recepcion_Descuento') || {}).value) || 0,
            total: Number((document.getElementById('Recepcion_Total') || {}).value) || 0,
        };
    }

    function hidden(name, value) {
        var i = document.createElement('input');
        i.type = 'hidden'; i.name = name; i.value = value == null ? '' : String(value);
        return i;
    }

    function serializar(items) {
        hiddenMount.innerHTML = '';
        items.forEach(function (it, i) {
            hiddenMount.appendChild(hidden('Items[' + i + '].ProductoId', it.productoId));
            hiddenMount.appendChild(hidden('Items[' + i + '].Cantidad', it.cantidad.toFixed(2)));
            hiddenMount.appendChild(hidden('Items[' + i + '].PrecioUnitario', it.precioUnitario.toFixed(2)));
            if (it.manejaGarrafaIndividual) {
                it.codigosGarrafa.forEach(function (c, j) {
                    hiddenMount.appendChild(hidden('Items[' + i + '].CodigosGarrafa[' + j + ']', c));
                });
            }
        });
    }

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        var items = itemsFromForm();
        var error = validarCliente(items);
        if (error) {
            if (typeof Swal !== 'undefined') Swal.fire({ icon: 'warning', title: 'Datos incompletos', text: error, confirmButtonColor: '#0d6efd' });
            else alert(error);
            return;
        }
        serializar(items);
        // Los hidden deben quedar dentro del <form> para que el binder los recorra.
        Array.from(hiddenMount.querySelectorAll('input')).forEach(function (i) { form.appendChild(i); });
        hiddenMount.innerHTML = '';

        if (typeof Swal === 'undefined') { form.submit(); return; }
        Swal.fire({
            title: '¿Confirmar recepción?',
            html: resumenHtml(items, leerTotales()),
            icon: 'question',
            showCancelButton: true,
            confirmButtonColor: '#0d6efd', cancelButtonColor: '#6c757d',
            confirmButtonText: 'Sí, confirmar', cancelButtonText: 'Cancelar',
            reverseButtons: true, width: 720,
        }).then(function (result) {
            if (result.isConfirmed) form.submit();
            else form.querySelectorAll('input[name^="Items["]').forEach(function (i) { i.remove(); });
        });
    });

    if (btnAdd) btnAdd.addEventListener('click', function () { agregarFila(); });
    if (dataPreCargada.length > 0) dataPreCargada.forEach(function (it) { agregarFila(it); });
    actualizarTotal();

    window.__recepciones = { agregarFila: agregarFila, itemsFromForm: itemsFromForm };
})();
