"use strict";

const $ = id => document.getElementById(id);
const pages = { controls: "Guard controls", behavior: "Behavior", recovery: "Recovery" };
const modButtons = [...document.querySelectorAll("[data-mod]")];
let model, mods = 0, saving = false;

function showPage(name) {
  for (const id of Object.keys(pages)) $(id).hidden = id !== name;
  for (const item of document.querySelectorAll("[data-page]")) {
    if (item.dataset.page === name) item.setAttribute("aria-current", "page");
    else item.removeAttribute("aria-current");
  }
  $("heading").textContent = pages[name];
  document.querySelector(".content").scrollTop = 0;
}
function message(text, field) {
  $("error").textContent = text;
  $("error").hidden = !text;
  for (const id of ["pin", "confirm"]) $(id).removeAttribute("aria-invalid");
  if (field) {
    showPage("controls");
    field.setAttribute("aria-invalid", "true");
    field.focus();
  }
  if (text) $("error").scrollIntoView({block:"nearest"});
}
function changes() {
  return { mods, key:Number($("key").value), pin:$("pin").value, confirm:$("confirm").value,
    fade:$("fade").checked, startup:model.startup === null ? null : $("startup").checked };
}
function update() {
  const edit = changes();
  const labels = [];
  for (const button of modButtons) {
    const selected = (mods & Number(button.dataset.mod)) !== 0;
    button.setAttribute("aria-pressed", String(selected));
    if (selected) labels.push(button.textContent);
  }
  labels.push($("key").selectedOptions[0].textContent);
  const caps = $("keycaps");
  caps.replaceChildren();
  labels.forEach((label, i) => {
    if (i) {
      const plus = document.createElement("span");
      plus.className = "plus"; plus.textContent = "+"; caps.append(plus);
    }
    const key = document.createElement("kbd");
    key.textContent = label; caps.append(key);
  });
  const dirty = edit.mods !== model.mods || edit.key !== model.key || edit.fade !== model.fade ||
    edit.startup !== model.startup || edit.pin !== "" || edit.confirm !== "";
  $("dirty").textContent = dirty ? "Unsaved changes" : "";
}
const invoke = (command, args) => window.__TAURI__.core.invoke(command, args);
async function closePanel() {
  await invoke("close_panel");
}
async function save() {
  if (!model || saving) return;
  message("");
  const edit = changes();
  if (!mods) { showPage("controls"); message("Choose at least one modifier."); modButtons[0].focus(); return; }
  if (!model.hasPin || edit.pin || edit.confirm) {
    if (!/^[0-9]{4,15}$/.test(edit.pin)) { message("Enter a PIN of 4–15 digits (0–9).", $("pin")); return; }
    if (edit.pin !== edit.confirm) { message("The PINs differ. Enter the same PIN in both fields.", $("confirm")); return; }
  }
  saving = true; $("save").disabled = true; $("save").textContent = "Saving…";
  try {
    await invoke("save_settings", {changes:edit});
    $("pin").value = $("confirm").value = "";
    await closePanel();
  } catch (error) {
    message(String(error));
  } finally {
    saving = false; $("save").disabled = false; $("save").textContent = "Save settings";
  }
}
for (const button of document.querySelectorAll("[data-page]"))
  button.addEventListener("click", () => showPage(button.dataset.page));
for (const button of modButtons)
  button.addEventListener("click", () => { if (model) { mods ^= Number(button.dataset.mod); message(""); update(); } });
for (const id of ["key", "pin", "confirm", "fade", "startup"])
  $(id).addEventListener("input", () => { if (model) { message(""); update(); } });
$("save").addEventListener("click", save);
$("cancel").addEventListener("click", () => { if (!saving) closePanel(); });
document.addEventListener("keydown", ev => {
  if (ev.key === "Escape" && !saving && ev.target.tagName !== "SELECT") closePanel();
  if ((ev.ctrlKey && ev.key.toLowerCase() === "s") || (ev.key === "Enter" && ev.target.type === "password")) {
    ev.preventDefault(); save();
  }
});
(async () => {
  try {
    model = await invoke("get_settings");
    mods = model.mods;
    for (const item of model.keys) $("key").add(new Option(item.label, item.value));
    $("key").value = model.key;
    $("fade").checked = model.fade;
    $("startup").checked = model.startup === true;
    $("startup").disabled = model.startup === null;
    $("version").textContent = "v" + model.version.split(".").slice(0,3).join(".");
    if (model.hasPin) {
      $("pin-label").textContent = "New PIN";
      $("pin-hint").textContent = "Use 4–15 digits. Leave both fields empty to keep your current PIN.";
    }
    if (model.notice) { $("notice").textContent = model.notice; $("notice").hidden = false; }
    update();
    $("save").disabled = false;
  } catch {
    message("Could not load settings. Close this window and reopen Settings from the tray.");
  } finally {
    $("panel").setAttribute("aria-busy", "false");
  }
})();
