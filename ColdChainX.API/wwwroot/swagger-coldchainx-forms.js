(function () {
    const orderExample = [
        { label: "Thung 5kg", capacityKg: 5, quantity: 4 },
        { label: "Thung 10kg", capacityKg: 10, quantity: 6 },
        { label: "Thung 22kg", capacityKg: 22, quantity: 3 }
    ];

    const qcExample = [
        { label: "Thung 5kg", quantity: 4, actualWeightKg: 22, lengthCm: 35, widthCm: 25, heightCm: 20 },
        { label: "Thung 10kg", quantity: 6, actualWeightKg: 63, lengthCm: 45, widthCm: 30, heightCm: 25 }
    ];

    function ensureStyles() {
        if (document.getElementById("ccx-swagger-form-style")) return;

        const style = document.createElement("style");
        style.id = "ccx-swagger-form-style";
        style.textContent = `
            .ccx-form-panel {
                margin: 12px 0 18px;
                padding: 14px;
                border: 1px solid #b7ebc6;
                border-radius: 4px;
                background: #ecfff2;
                color: #1f2937;
            }
            .ccx-form-title {
                margin: 0 0 4px;
                font-size: 14px;
                font-weight: 700;
            }
            .ccx-form-help {
                margin: 0 0 12px;
                font-size: 12px;
                color: #4b5563;
            }
            .ccx-lines-header {
                display: flex;
                align-items: center;
                justify-content: space-between;
                margin: 8px 0;
                font-weight: 700;
                font-size: 13px;
            }
            .ccx-add-line {
                border: 0;
                border-radius: 3px;
                background: #22c55e;
                color: white;
                padding: 6px 10px;
                font-size: 12px;
                font-weight: 700;
                cursor: pointer;
            }
            .ccx-line-card {
                margin: 8px 0;
                padding: 12px;
                border: 1px solid #d1fae5;
                border-radius: 4px;
                background: #ffffff;
            }
            .ccx-line-top {
                display: flex;
                justify-content: space-between;
                align-items: center;
                margin-bottom: 10px;
                font-size: 12px;
                font-weight: 700;
            }
            .ccx-remove-line {
                border: 0;
                background: transparent;
                color: #dc2626;
                cursor: pointer;
                font-weight: 700;
            }
            .ccx-field-grid {
                display: grid;
                grid-template-columns: repeat(4, minmax(120px, 1fr));
                gap: 10px;
            }
            .ccx-field-grid.qc {
                grid-template-columns: repeat(6, minmax(90px, 1fr));
            }
            .ccx-field label {
                display: block;
                margin-bottom: 4px;
                font-size: 11px;
                color: #374151;
                font-weight: 700;
            }
            .ccx-field input {
                width: 100%;
                min-height: 32px;
                padding: 6px 8px;
                border: 1px solid #d1d5db;
                border-radius: 3px;
                background: #fff;
            }
            .ccx-total {
                margin-top: 10px;
                padding: 8px 10px;
                background: #dffbea;
                border-radius: 3px;
                font-size: 12px;
                font-weight: 700;
            }
            .ccx-json-preview {
                margin-top: 8px;
                padding: 8px;
                max-height: 90px;
                overflow: auto;
                background: #111827;
                color: #d1fae5;
                border-radius: 3px;
                font-size: 11px;
                white-space: pre-wrap;
            }
            .ccx-hidden-swagger-row {
                display: none !important;
            }
            @media (max-width: 1100px) {
                .ccx-field-grid,
                .ccx-field-grid.qc {
                    grid-template-columns: repeat(2, minmax(120px, 1fr));
                }
            }
        `;
        document.head.appendChild(style);
    }

    function findOperation(method, path) {
        const blocks = Array.from(document.querySelectorAll(".opblock"));
        return blocks.find(block => {
            const methodText = block.querySelector(".opblock-summary-method")?.textContent?.trim().toUpperCase();
            const pathText = block.querySelector(".opblock-summary-path")?.textContent?.trim();
            return methodText === method.toUpperCase() && pathText === path;
        });
    }

    function findFormInput(operation, fieldName) {
        const controls = Array.from(operation.querySelectorAll("input, textarea"));
        return controls.find(input => {
            const placeholder = input.getAttribute("placeholder");
            const name = input.getAttribute("name");
            const aria = input.getAttribute("aria-label");
            return [placeholder, name, aria].some(value => value && value.toLowerCase() === fieldName.toLowerCase());
        });
    }

    function hideSwaggerField(input) {
        const row = input.closest("tr") || input.closest(".parameters-col_description")?.parentElement || input.closest(".parameter__name")?.parentElement;
        if (row) row.classList.add("ccx-hidden-swagger-row");
    }

    function setSwaggerValue(input, value) {
        if (!input) return;

        const proto = input instanceof HTMLTextAreaElement
            ? window.HTMLTextAreaElement.prototype
            : window.HTMLInputElement.prototype;
        const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set;

        if (setter) setter.call(input, value);
        else input.value = value;

        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
    }

    function numberValue(input, fallback) {
        const value = Number(input.value);
        return Number.isFinite(value) && value > 0 ? value : fallback;
    }

    function textValue(input, fallback) {
        const value = input.value.trim();
        return value.length > 0 ? value : fallback;
    }

    function createInput(label, type, value) {
        const wrapper = document.createElement("div");
        wrapper.className = "ccx-field";
        wrapper.innerHTML = `<label>${label}</label>`;
        const input = document.createElement("input");
        input.type = type;
        input.value = value;
        if (type === "number") {
            input.min = "0";
            input.step = "0.01";
        }
        wrapper.appendChild(input);
        return { wrapper, input };
    }

    function createOrderLine(container, initial, sync) {
        const card = document.createElement("div");
        card.className = "ccx-line-card";

        const top = document.createElement("div");
        top.className = "ccx-line-top";
        top.innerHTML = `<span>Package size</span>`;
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "ccx-remove-line";
        remove.textContent = "Remove";
        top.appendChild(remove);

        const grid = document.createElement("div");
        grid.className = "ccx-field-grid";

        const label = createInput("Label", "text", initial.label);
        const capacity = createInput("Capacity kg", "number", initial.capacityKg);
        const quantity = createInput("Quantity", "number", initial.quantity);

        grid.append(label.wrapper, capacity.wrapper, quantity.wrapper);
        card.append(top, grid);
        container.appendChild(card);

        const controls = [label.input, capacity.input, quantity.input];
        controls.forEach(input => input.addEventListener("input", sync));
        remove.addEventListener("click", () => {
            card.remove();
            sync();
        });

        return {
            card,
            read: () => ({
                label: textValue(label.input, "Package"),
                capacityKg: numberValue(capacity.input, 0),
                quantity: Math.round(numberValue(quantity.input, 0))
            })
        };
    }

    function createQcLine(container, initial, sync) {
        const card = document.createElement("div");
        card.className = "ccx-line-card";

        const top = document.createElement("div");
        top.className = "ccx-line-top";
        top.innerHTML = `<span>Actual package line</span>`;
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "ccx-remove-line";
        remove.textContent = "Remove";
        top.appendChild(remove);

        const grid = document.createElement("div");
        grid.className = "ccx-field-grid qc";

        const label = createInput("Label", "text", initial.label);
        const quantity = createInput("Quantity", "number", initial.quantity);
        const weight = createInput("Actual weight kg", "number", initial.actualWeightKg);
        const length = createInput("Length cm", "number", initial.lengthCm);
        const width = createInput("Width cm", "number", initial.widthCm);
        const height = createInput("Height cm", "number", initial.heightCm);

        grid.append(label.wrapper, quantity.wrapper, weight.wrapper, length.wrapper, width.wrapper, height.wrapper);
        card.append(top, grid);
        container.appendChild(card);

        const controls = [label.input, quantity.input, weight.input, length.input, width.input, height.input];
        controls.forEach(input => input.addEventListener("input", sync));
        remove.addEventListener("click", () => {
            card.remove();
            sync();
        });

        return {
            card,
            read: () => ({
                label: textValue(label.input, "Package"),
                quantity: Math.round(numberValue(quantity.input, 0)),
                actualWeightKg: numberValue(weight.input, 0),
                lengthCm: numberValue(length.input, 0),
                widthCm: numberValue(width.input, 0),
                heightCm: numberValue(height.input, 0)
            })
        };
    }

    function mountOrderForm(operation) {
        if (operation.querySelector(".ccx-order-form")) return;

        const targetInput = findFormInput(operation, "Package_Lines");
        if (!targetInput) return;
        hideSwaggerField(targetInput);

        const panel = document.createElement("div");
        panel.className = "ccx-form-panel ccx-order-form";
        panel.innerHTML = `
            <p class="ccx-form-title">Create order with multiple package sizes</p>
            <p class="ccx-form-help">Add one row per package size. Swagger will submit the generated Package_Lines JSON.</p>
            <div class="ccx-lines-header">
                <span>Package sizes</span>
                <button type="button" class="ccx-add-line">+ Add size</button>
            </div>
            <div class="ccx-lines"></div>
            <div class="ccx-total"></div>
            <pre class="ccx-json-preview"></pre>
        `;

        const body = operation.querySelector(".opblock-body") || operation;
        body.prepend(panel);

        const linesContainer = panel.querySelector(".ccx-lines");
        const addButton = panel.querySelector(".ccx-add-line");
        const total = panel.querySelector(".ccx-total");
        const preview = panel.querySelector(".ccx-json-preview");
        const lines = [];

        function sync() {
            const payload = lines
                .filter(line => document.body.contains(line.card))
                .map(line => line.read())
                .filter(line => line.capacityKg > 0 && line.quantity > 0);

            const totalQty = payload.reduce((sum, line) => sum + line.quantity, 0);
            const totalWeight = payload.reduce((sum, line) => sum + line.capacityKg * line.quantity, 0);
            const json = JSON.stringify(payload);

            total.textContent = `Order total: ${totalQty} packages | ${totalWeight.toFixed(2)} kg expected`;
            preview.textContent = json;
            setSwaggerValue(targetInput, json);
        }

        addButton.addEventListener("click", () => {
            lines.push(createOrderLine(linesContainer, { label: "Thung khac", capacityKg: 1, quantity: 1 }, sync));
            sync();
        });

        orderExample.forEach(item => lines.push(createOrderLine(linesContainer, item, sync)));
        sync();
    }

    function mountQcForm(operation) {
        if (operation.querySelector(".ccx-qc-form")) return;

        const targetInput = findFormInput(operation, "Actual_Package_Lines") || findFormInput(operation, "ActualPackageLinesJson");
        if (!targetInput) return;
        hideSwaggerField(targetInput);

        const panel = document.createElement("div");
        panel.className = "ccx-form-panel ccx-qc-form";
        panel.innerHTML = `
            <p class="ccx-form-title">Warehouse QC actual package lines</p>
            <p class="ccx-form-help">Enter measured package groups. Swagger will submit the generated Actual_Package_Lines JSON.</p>
            <div class="ccx-lines-header">
                <span>Actual package lines</span>
                <button type="button" class="ccx-add-line">+ Add line</button>
            </div>
            <div class="ccx-lines"></div>
            <div class="ccx-total"></div>
            <pre class="ccx-json-preview"></pre>
        `;

        const body = operation.querySelector(".opblock-body") || operation;
        body.prepend(panel);

        const linesContainer = panel.querySelector(".ccx-lines");
        const addButton = panel.querySelector(".ccx-add-line");
        const total = panel.querySelector(".ccx-total");
        const preview = panel.querySelector(".ccx-json-preview");
        const lines = [];

        function sync() {
            const payload = lines
                .filter(line => document.body.contains(line.card))
                .map(line => line.read())
                .filter(line => line.quantity > 0 && line.actualWeightKg > 0 && line.lengthCm > 0 && line.widthCm > 0 && line.heightCm > 0);

            const totalQty = payload.reduce((sum, line) => sum + line.quantity, 0);
            const totalWeight = payload.reduce((sum, line) => sum + line.actualWeightKg, 0);
            const totalCbm = payload.reduce((sum, line) => sum + (line.lengthCm * line.widthCm * line.heightCm * line.quantity / 1000000), 0);
            const json = JSON.stringify(payload);

            total.textContent = `QC total: ${totalQty} packages | ${totalWeight.toFixed(2)} kg actual | ${totalCbm.toFixed(4)} CBM`;
            preview.textContent = json;
            setSwaggerValue(targetInput, json);
        }

        addButton.addEventListener("click", () => {
            lines.push(createQcLine(linesContainer, { label: "Thung khac", quantity: 1, actualWeightKg: 1, lengthCm: 1, widthCm: 1, heightCm: 1 }, sync));
            sync();
        });

        qcExample.forEach(item => lines.push(createQcLine(linesContainer, item, sync)));
        sync();
    }

    function mountForms() {
        ensureStyles();
        const orderOperation = findOperation("POST", "/api/orders");
        if (orderOperation) mountOrderForm(orderOperation);

        const qcOperation = findOperation("POST", "/api/Inbound/qc");
        if (qcOperation) mountQcForm(qcOperation);
    }

    const observer = new MutationObserver(() => {
        window.requestAnimationFrame(mountForms);
    });

    observer.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener("load", () => setTimeout(mountForms, 500));
})();
