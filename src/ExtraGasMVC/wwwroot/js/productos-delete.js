/**
 * Issue #147 slice 3 item 2: enable submit only when the type-to-confirm
 * input matches the product code. Wired in Views/Productos/Delete.cshtml
 * (inline bootstrapper) — this file documents the canonical pattern and
 * is included for future reuse if the slice extends to other modules
 * (e.g. Proveedor / Cliente delete with type-to-confirm).
 *
 * El bloque inline en Delete.cshtml es self-contained porque el patrón es
 * específico del Delete de Producto y solo se usa en esa view. Este
 * archivo queda como referencia del contrato: input con data-expected-code
 * + submit button → enable submit solo cuando input.value === expected.
 */
(function () {
    document.querySelectorAll('.js-producto-delete-form').forEach(function (form) {
        var input = form.querySelector('.js-producto-confirm-input');
        var submit = form.querySelector('.js-producto-confirm-submit');
        if (!input || !submit) return;
        var expected = (input.getAttribute('data-expected-code') || '').trim();
        function sync() {
            var value = (input.value || '').trim();
            submit.disabled = value !== expected;
        }
        input.addEventListener('input', sync);
        sync();
    });
})();
