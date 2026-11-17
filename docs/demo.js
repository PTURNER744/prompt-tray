const items = [
  { label: "Summarise this meeting", category: "Work", kind: "Text", value: "Summarise this meeting in three bullets. Include decisions, owners and next steps." },
  { label: "Brand guidelines.pdf", category: "Work", kind: "File link" },
  { label: "Delivery address", category: "Personal", kind: "Text", value: "18 Market Lane\nBristol BS1 2AB" },
  { label: "Invoice template.xlsx", category: "Admin", kind: "Stored file" }
];
const search = document.querySelector("#search");
const results = document.querySelector("#results");
const action = document.querySelector("#use-item");
const status = document.querySelector("#demo-status");
let matching = items;
let selected = 0;

function render() {
  results.replaceChildren();
  const item = matching[selected];
  for (const [index, entry] of matching.entries()) {
    const row = document.createElement("button");
    row.className = "result";
    row.type = "button";
    row.setAttribute("aria-pressed", String(index === selected));
    const icon = document.createElement("span");
    icon.className = "result-icon";
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = entry.kind === "Text" ? "≡" : "↗";
    const label = document.createElement("span");
    const name = document.createElement("strong");
    name.textContent = entry.label;
    const kind = document.createElement("small");
    kind.textContent = entry.kind;
    label.append(name, kind);
    const category = document.createElement("span");
    category.className = "category";
    category.textContent = entry.category;
    row.append(icon, label, category);
    row.addEventListener("click", () => {
      if (selected === index) return;
      selected = index;
      status.textContent = "";
      render();
      results.children[selected].focus();
    });
    results.append(row);
  }
  if (!item) {
    const empty = document.createElement("p");
    empty.className = "empty";
    empty.textContent = "No matching items.";
    results.append(empty);
  }
  action.disabled = !item;
  action.textContent = item && item.kind !== "Text" ? "Open file" : "Copy text";
}

action.addEventListener("click", async () => {
  const item = matching[selected];
  if (!item) return;
  if (item.kind !== "Text") {
    status.textContent = "The desktop app opens " + item.label + ".";
  } else {
    try {
      await navigator.clipboard.writeText(item.value);
      status.textContent = "Sample text copied.";
    } catch {
      status.textContent = item.value;
    }
  }
});

search.addEventListener("input", () => {
  const query = search.value.toLocaleLowerCase().trim();
  matching = items.filter(item => [item.label, item.kind, item.category, item.value].join(" ").toLocaleLowerCase().includes(query));
  selected = 0;
  status.textContent = "";
  render();
});
search.addEventListener("keydown", event => {
  if (!matching.length) return;
  if (event.key === "ArrowUp" || event.key === "ArrowDown") {
    event.preventDefault();
    selected = Math.max(0, Math.min(matching.length - 1, selected + (event.key === "ArrowDown" ? 1 : -1)));
    status.textContent = "";
    render();
  } else if (event.key === "Enter") {
    event.preventDefault();
    action.click();
  }
});
render();
