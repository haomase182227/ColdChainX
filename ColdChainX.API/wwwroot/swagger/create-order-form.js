(function () {
  "use strict";

  const operationSelector = "#operations-Order-post_api_orders";
  const categories = [
    "MEAT_SEAFOOD",
    "FROZEN_FRUITS_VEGGIES",
    "ICE_CREAM_BEVERAGES",
    "PHARMACEUTICALS",
    "RAW_MATERIALS_OTHERS"
  ];
  const packagingTypes = ["Pallet", "Thùng", "Bin", "Bao", "Plastic Box", "Foam Box", "Carton Box"];

  function optionMarkup(values) {
    return values.map(value => `<option value="${value}">${value}</option>`).join("");
  }

  function getBearerToken() {
    try {
      const authorized = window.ui?.authSelectors?.authorized?.();
      const authObject = authorized?.toJS ? authorized.toJS() : authorized;
      const bearer = authObject?.Bearer || Object.values(authObject || {})[0];
      const rawValue = bearer?.value || "";
      if (!rawValue) return "";
      return rawValue.toLowerCase().startsWith("bearer ") ? rawValue : `Bearer ${rawValue}`;
    } catch (_) {
      return "";
    }
  }

  function numberValue(container, selector) {
    return Number(container.querySelector(selector).value || 0);
  }

  function updateTotals(panel) {
    let totalQuantity = 0;
    let totalWeight = 0;
    let totalCbm = 0;

    panel.querySelectorAll(".ccx-variant-card").forEach(card => {
      const quantity = numberValue(card, ".ccx-quantity");
      const unitWeight = numberValue(card, ".ccx-unit-weight");
      const length = numberValue(card, ".ccx-length");
      const width = numberValue(card, ".ccx-width");
      const height = numberValue(card, ".ccx-height");
      const lineWeight = quantity * unitWeight;
      const lineCbm = length * width * height * quantity / 1000000;

      totalQuantity += quantity;
      totalWeight += lineWeight;
      totalCbm += lineCbm;

      card.querySelector(".ccx-variant-summary").textContent =
        `Size total: ${lineWeight.toFixed(2)} kg | ${lineCbm.toFixed(4)} CBM`;
    });

    panel.querySelector(".ccx-order-summary").textContent =
      `Order total: ${totalQuantity} packages | ${totalWeight.toFixed(2)} kg | ${totalCbm.toFixed(4)} CBM`;
  }

  function renumberVariants(panel) {
    panel.querySelectorAll(".ccx-variant-card").forEach((card, index) => {
      card.querySelector(".ccx-variant-title").textContent = `Package size ${index + 1}`;
      card.dataset.index = String(index);
    });
    updateTotals(panel);
  }

  function createVariantCard(panel) {
    const card = document.createElement("section");
    card.className = "ccx-variant-card";
    card.innerHTML = `
      <div class="ccx-variant-header">
        <h5 class="ccx-variant-title">Package size</h5>
        <button class="ccx-remove-size" type="button">Remove</button>
      </div>
      <div class="ccx-variant-grid">
        <label>Size name
          <input class="ccx-variant-name" type="text" maxlength="100" placeholder="Small box">
        </label>
        <label>Packaging type *
          <select class="ccx-packaging-type" required>${optionMarkup(packagingTypes)}</select>
        </label>
        <label>Quantity *
          <input class="ccx-quantity" type="number" min="1" step="1" value="1" required>
        </label>
        <label>Weight per package (kg) *
          <input class="ccx-unit-weight" type="number" min="0.01" step="0.01" required>
        </label>
        <label>Length (cm) *
          <input class="ccx-length" type="number" min="0.01" step="0.01" required>
        </label>
        <label>Width (cm) *
          <input class="ccx-width" type="number" min="0.01" step="0.01" required>
        </label>
        <label>Height (cm) *
          <input class="ccx-height" type="number" min="0.01" step="0.01" required>
        </label>
      </div>
      <div class="ccx-file-grid">
        <label>Legal documents for this size
          <input class="ccx-legal-files" type="file" multiple accept=".pdf,.png,.jpg,.jpeg,.webp">
        </label>
        <label>Cargo photos for this size
          <input class="ccx-cargo-files" type="file" multiple accept="image/png,image/jpeg,image/webp">
        </label>
      </div>
      <div class="ccx-variant-summary">Size total: 0.00 kg | 0.0000 CBM</div>`;

    card.querySelector(".ccx-remove-size").addEventListener("click", () => {
      if (panel.querySelectorAll(".ccx-variant-card").length === 1) {
        window.alert("An order must contain at least one package size.");
        return;
      }
      card.remove();
      renumberVariants(panel);
    });

    card.querySelectorAll('input[type="number"]').forEach(input => {
      input.addEventListener("input", () => updateTotals(panel));
    });

    return card;
  }

  function appendFiles(formData, fieldName, fileInput) {
    Array.from(fileInput.files || []).forEach(file => formData.append(fieldName, file, file.name));
  }

  function buildRequestBody(panel) {
    const formData = new FormData();
    const customerId = panel.querySelector(".ccx-customer-id").value.trim();
    if (customerId) formData.append("Customer_ID", customerId);
    formData.append("Item_Name", panel.querySelector(".ccx-item-name").value.trim());
    formData.append("Category", panel.querySelector(".ccx-category").value);
    formData.append("Temp_Condition", panel.querySelector(".ccx-temperature").value);
    formData.append("Dest_Address_Text", panel.querySelector(".ccx-address").value.trim());
    formData.append("Schedule_ID", panel.querySelector(".ccx-schedule-id").value.trim());
    formData.append("Dropoff_Stop_ID", panel.querySelector(".ccx-dropoff-stop-id").value.trim());
    formData.append("Has_Strong_Odor", String(panel.querySelector(".ccx-strong-odor").checked));
    formData.append("Is_Stackable", String(panel.querySelector(".ccx-stackable").checked));

    panel.querySelectorAll(".ccx-variant-card").forEach((card, index) => {
      const prefix = `PackageVariants[${index}]`;
      formData.append(`${prefix}.VariantName`, card.querySelector(".ccx-variant-name").value.trim());
      formData.append(`${prefix}.PackagingType`, card.querySelector(".ccx-packaging-type").value);
      formData.append(`${prefix}.Quantity`, card.querySelector(".ccx-quantity").value);
      formData.append(`${prefix}.ExpectedUnitWeightKg`, card.querySelector(".ccx-unit-weight").value);
      formData.append(`${prefix}.LengthCm`, card.querySelector(".ccx-length").value);
      formData.append(`${prefix}.WidthCm`, card.querySelector(".ccx-width").value);
      formData.append(`${prefix}.HeightCm`, card.querySelector(".ccx-height").value);
      appendFiles(formData, `${prefix}.LegalDocuments`, card.querySelector(".ccx-legal-files"));
      appendFiles(formData, `${prefix}.CargoPhotos`, card.querySelector(".ccx-cargo-files"));
    });

    return formData;
  }

  async function submitOrder(panel) {
    const form = panel.querySelector("form");
    if (!form.reportValidity()) return;

    const result = panel.querySelector(".ccx-order-result");
    const button = panel.querySelector(".ccx-submit-order");
    const authorization = getBearerToken();
    if (!authorization) {
      result.textContent = "Please click Authorize in Swagger and enter a Customer or Sales JWT first.";
      return;
    }

    button.disabled = true;
    button.textContent = "Creating...";
    result.textContent = "Sending multipart/form-data request...";

    try {
      const response = await fetch("/api/orders", {
        method: "POST",
        headers: { Authorization: authorization },
        body: buildRequestBody(panel),
        credentials: "same-origin"
      });
      const rawBody = await response.text();
      let formattedBody = rawBody;
      try {
        formattedBody = JSON.stringify(JSON.parse(rawBody), null, 2);
      } catch (_) {
        // Keep non-JSON response text as-is.
      }
      result.textContent = `HTTP ${response.status} ${response.statusText}\n\n${formattedBody}`;
    } catch (error) {
      result.textContent = `Request failed: ${error?.message || error}`;
    } finally {
      button.disabled = false;
      button.textContent = "Create order";
    }
  }

  function createPanel() {
    const panel = document.createElement("section");
    panel.className = "ccx-order-form";
    panel.innerHTML = `
      <h4>Create order with multiple package sizes</h4>
      <p class="ccx-order-form-note">
        One order contains one item/category/temperature profile. Add up to 20 package sizes;
        each size has its own weight, dimensions, legal documents and cargo photos.
        Customer ID is only required for an internal user creating an order on behalf of a customer.
      </p>
      <form>
        <div class="ccx-order-grid">
          <label>Customer ID (internal users only)
            <input class="ccx-customer-id" type="text" pattern="[0-9a-fA-F-]{36}">
          </label>
          <label>Item name *
            <input class="ccx-item-name" type="text" maxlength="150" required>
          </label>
          <label>Category *
            <select class="ccx-category" required>${optionMarkup(categories)}</select>
          </label>
          <label>Temperature (°C) *
            <input class="ccx-temperature" type="number" min="-18" max="-5" step="0.1" value="-18" required>
          </label>
          <label>Schedule ID *
            <input class="ccx-schedule-id" type="text" pattern="[0-9a-fA-F-]{36}" required>
          </label>
          <label>Drop-off stop ID *
            <input class="ccx-dropoff-stop-id" type="text" pattern="[0-9a-fA-F-]{36}" required>
          </label>
          <label>Destination address *
            <input class="ccx-address" type="text" maxlength="500" required>
          </label>
        </div>
        <div class="ccx-checkboxes">
          <label><input class="ccx-strong-odor" type="checkbox"> Strong odor</label>
          <label><input class="ccx-stackable" type="checkbox" checked> Stackable</label>
        </div>
        <div class="ccx-variants-toolbar">
          <h4>Package sizes</h4>
          <button class="ccx-add-size" type="button">+ Add size</button>
        </div>
        <div class="ccx-variant-list"></div>
        <div class="ccx-order-summary">Order total: 0 packages | 0.00 kg | 0.0000 CBM</div>
        <div class="ccx-order-actions">
          <span>Uses the JWT entered through Swagger's Authorize button.</span>
          <button class="ccx-submit-order" type="button">Create order</button>
        </div>
        <pre class="ccx-order-result">No request sent yet.</pre>
      </form>`;

    panel.querySelector(".ccx-add-size").addEventListener("click", () => {
      if (panel.querySelectorAll(".ccx-variant-card").length >= 20) {
        window.alert("An order cannot contain more than 20 package sizes.");
        return;
      }
      panel.querySelector(".ccx-variant-list").appendChild(createVariantCard(panel));
      renumberVariants(panel);
    });
    panel.querySelector(".ccx-submit-order").addEventListener("click", () => submitOrder(panel));
    panel.querySelector(".ccx-variant-list").appendChild(createVariantCard(panel));
    renumberVariants(panel);
    return panel;
  }

  function mount() {
    document.querySelectorAll(operationSelector).forEach(operation => {
      const body = operation.querySelector(".opblock-body");
      if (!body) return;

      body.querySelectorAll(":scope > .opblock-section, :scope > .execute-wrapper").forEach(element => {
        element.classList.add("ccx-hidden-default-order-ui");
      });

      if (body.querySelector(".ccx-order-form")) return;

      const responses = body.querySelector(".responses-wrapper");
      body.insertBefore(createPanel(), responses || null);
    });
  }

  new MutationObserver(mount).observe(document.documentElement, { childList: true, subtree: true });
  mount();
})();
