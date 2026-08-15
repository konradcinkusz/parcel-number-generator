// Operator console. Talks only to its own origin — the BFF proxies to the services
// (FRONTEND-BFF §1) — and renders everything through textContent, never markup, because
// notification bodies are operator-typed text.

"use strict";

const generator = (path) => `/api/generator/${path}`;
const notifications = (path) => (path ? `/api/notifications/${path}` : "/api/notifications");

const SEVERITY = {
  0: { key: "unspecified", label: "Unspecified" },
  1: { key: "information", label: "Information" },
  2: { key: "warning", label: "Warning" },
  3: { key: "error", label: "Error" },
};

const RAISED_BY = {
  0: "manual", 1: "received", 2: "put-away", 3: "picked", 4: "packed", 5: "dispatched", 6: "exception",
};

const state = { page: 1, limit: 25 };

const $ = (id) => document.getElementById(id);

// ---------------------------------------------------------------- parcel numbers

// Mirrors ADR-0003: eight-digit payload, Luhn check digit, PNG- prefix. Presentation
// only — the services stay authoritative about what parses.
function luhnCheckDigit(payload) {
  let sum = 0;
  let doubling = true;
  for (let i = payload.length - 1; i >= 0; i--) {
    let digit = payload.charCodeAt(i) - 48;
    if (doubling) {
      digit *= 2;
      if (digit > 9) digit -= 9;
    }
    sum += digit;
    doubling = !doubling;
  }
  return (10 - (sum % 10)) % 10;
}

function canonicalForm(number) {
  const payload = String(number).padStart(8, "0");
  return `PNG-${payload}-${luhnCheckDigit(payload)}`;
}

// Accepts the dialects the estate speaks and yields the bare integer the generator
// understands, or null when the input is not a parcel number.
function parseDialect(raw) {
  let compact = raw.toUpperCase().replace(/[\s\-/._]/g, "");
  if (compact.startsWith("PNG")) compact = compact.slice(3);
  else if (compact.startsWith("WMS")) compact = compact.slice(3);
  if (!/^\d+$/.test(compact)) return null;
  if (compact.length === 9) {
    const payload = compact.slice(0, 8);
    if (Number(compact[8]) !== luhnCheckDigit(payload)) return null;
    compact = payload;
  }
  if (compact.length > 8) return null;
  return Number(compact);
}

// ---------------------------------------------------------------- plumbing

async function callApi(url, options) {
  const response = await fetch(url, options);
  if (response.ok) {
    return response.status === 204 ? null : response.json();
  }

  let detail = `${response.status} ${response.statusText}`;
  try {
    const problem = await response.json();
    if (problem) {
      if (problem.errors) {
        detail = Object.values(problem.errors).flat().join(" ");
      } else {
        detail = problem.detail || problem.title || detail;
      }
    }
  } catch { /* body was not JSON; keep the status line */ }

  const error = new Error(detail);
  error.status = response.status;
  error.retryAfter = response.headers.get("Retry-After");
  throw error;
}

let toastTimer;
function toast(message) {
  const el = $("toast");
  el.textContent = message;
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.hidden = true; }, 6000);
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

// ---------------------------------------------------------------- pool

async function refreshPool() {
  const pool = await callApi(generator("pool"));

  $("stat-capacity").textContent = pool.capacity.toLocaleString();
  $("stat-used").textContent = pool.used.toLocaleString();
  $("stat-remaining").textContent = pool.remaining.toLocaleString();
  $("stat-density").textContent = `${(pool.density * 100).toFixed(1)}%`;

  const strategy = $("pool-strategy");
  strategy.textContent = pool.strategy;
  strategy.hidden = false;

  const percent = pool.capacity === 0 ? 0 : (pool.used / pool.capacity) * 100;
  $("meter-fill").style.width = `${Math.min(100, percent)}%`;
  $("meter-label").textContent = pool.remaining === 0
    ? "exhausted"
    : `${percent.toFixed(percent > 0 && percent < 1 ? 2 : 1)}% used`;
  $("meter").setAttribute("aria-label", `Pool utilization: ${percent.toFixed(1)} percent used`);

  const exclusions = pool.exclusions.length === 0
    ? "no exclusions"
    : `excluding ${pool.exclusions.map((range) => `${range.from.toLocaleString()}–${range.to.toLocaleString()}`).join(", ")}`;
  $("pool-range").textContent =
    `Pool ${pool.from.toLocaleString()}–${pool.to.toLocaleString()}, ${exclusions}.`;
}

// ---------------------------------------------------------------- allocation

async function allocate(event) {
  event.preventDefault();
  const count = Number($("allocate-count").value);

  try {
    const result = await callApi(generator(`parcel-numbers?count=${count}`), { method: "POST" });

    $("allocate-result").hidden = false;
    $("allocate-summary").textContent = result.complete
      ? `Issued ${result.numbers.length} number${result.numbers.length === 1 ? "" : "s"}.`
      : `Issued ${result.numbers.length} of ${result.requested} requested — ${result.reason ?? "the pool could not supply the rest"}`;

    const list = $("allocate-numbers");
    list.replaceChildren();
    for (const number of result.numbers) {
      const canonical = canonicalForm(number);
      const item = el("li");
      item.append(el("span", "number-canonical", canonical), el("span", "number-raw", `#${number}`));
      const copy = el("button", "copy", "copy");
      copy.type = "button";
      copy.title = `Copy ${canonical}`;
      copy.addEventListener("click", async () => {
        await navigator.clipboard.writeText(canonical);
        copy.textContent = "copied";
        setTimeout(() => { copy.textContent = "copy"; }, 1200);
      });
      item.append(copy);
      list.append(item);
    }

    await refreshPool();
  } catch (error) {
    if (error.status === 409) toast("Pool exhausted — every number in the configured pool has been issued.");
    else if (error.status === 503) toast(`Allocation contended — retry in ${error.retryAfter ?? "a moment"}s.`);
    else if (error.status === 429) toast("Rate limited — slow down and retry shortly.");
    else toast(error.message);
  }
}

// ---------------------------------------------------------------- lookup

async function lookup(event) {
  event.preventDefault();
  const raw = $("lookup-number").value.trim();
  const output = $("lookup-result");

  const number = parseDialect(raw);
  if (number === null) {
    output.hidden = false;
    output.textContent = `“${raw}” is not a recognized parcel number in any accepted dialect.`;
    return;
  }

  try {
    const status = await callApi(generator(`parcel-numbers/${number}`));
    output.hidden = false;
    output.textContent = status.inPool
      ? `${canonicalForm(number)} — ${status.used ? "issued" : "not issued yet"}, allocatable pool number.`
      : `${canonicalForm(number)} — outside the allocatable pool${status.used ? ", yet marked used (investigate)" : ""}.`;
  } catch (error) {
    toast(error.message);
  }
}

// ---------------------------------------------------------------- notifications

function severityBadge(severity) {
  const info = SEVERITY[severity] ?? SEVERITY[0];
  const badge = el("span", `badge ${info.key}`);
  badge.append(el("span", "badge-dot"), document.createTextNode(info.label));
  return badge;
}

function notificationRow(notification) {
  const row = el("li", "notification");

  row.append(severityBadge(notification.severity));

  const body = el("p", "notification-body", notification.body);
  if (notification.pinned) body.prepend(el("span", "pin", "📌 "));
  row.append(body);

  const ackCell = el("div", "ack-state");
  if (notification.acknowledgedAt) {
    const when = new Date(notification.acknowledgedAt).toLocaleString();
    const who = notification.acknowledgedBy ? ` by ${notification.acknowledgedBy}` : "";
    ackCell.append(el("span", "acked", `✓ acknowledged`), el("div", null, `${when}${who}`));
  } else if (notification.acknowledgementRequired) {
    const button = el("button", "ack primary", "Acknowledge");
    button.type = "button";
    button.addEventListener("click", async () => {
      button.disabled = true;
      try {
        await callApi(notifications(`${notification.id}/acknowledgement`), { method: "POST" });
        await refreshNotifications();
      } catch (error) {
        button.disabled = false;
        toast(error.message);
      }
    });
    ackCell.append(button);
  } else {
    ackCell.append(el("span", null, "no acknowledgement needed"));
  }
  row.append(ackCell);

  const meta = el("div", "notification-meta");
  if (notification.parcelNumber) meta.append(el("span", "parcel-ref", notification.parcelNumber));
  meta.append(el("span", null, RAISED_BY[notification.raisedBy] ?? "event"));
  meta.append(el("span", null, new Date(notification.createdAt).toLocaleString()));
  row.append(meta);

  return row;
}

async function refreshNotifications() {
  const query = new URLSearchParams({ page: String(state.page), limit: String(state.limit) });
  const parcel = $("filter-parcel").value.trim();
  if (parcel) {
    // The service accepts full dialects but not bare short numbers; canonicalize what we
    // can client-side so "27495" filters as PNG-00027495-1 instead of being rejected.
    const parsed = parseDialect(parcel);
    query.set("parcelNumber", parsed === null ? parcel : canonicalForm(parsed));
  }
  if ($("filter-outstanding").checked) query.set("outstandingOnly", "true");
  const severity = $("filter-severity").value;
  if (severity) query.set("severity", severity);

  const pageData = await callApi(`${notifications("")}?${query}`);

  const list = $("notification-list");
  list.replaceChildren(...pageData.items.map(notificationRow));
  $("notifications-empty").hidden = pageData.items.length > 0;

  const counts = $("notifications-counts");
  counts.hidden = false;
  counts.textContent = `${pageData.total.toLocaleString()} total · ${pageData.outstanding.toLocaleString()} outstanding`;

  const lastPage = Math.max(1, Math.ceil(pageData.total / pageData.limit));
  state.page = pageData.page;
  $("page-label").textContent = `page ${pageData.page} of ${lastPage}`;
  $("page-prev").disabled = pageData.page <= 1;
  $("page-next").disabled = pageData.page >= lastPage;
}

async function raise(event) {
  event.preventDefault();

  const request = {
    body: $("raise-body").value.trim(),
    severity: Number($("raise-severity").value),
    acknowledgementRequired: $("raise-ack").checked,
    pinned: $("raise-pinned").checked,
  };
  const parcel = $("raise-parcel").value.trim();
  if (parcel) request.parcelNumber = parcel;

  try {
    await callApi(notifications(""), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });
    $("raise-body").value = "";
    $("raise-parcel").value = "";
    state.page = 1;
    await refreshNotifications();
  } catch (error) {
    toast(error.message);
  }
}

// ---------------------------------------------------------------- wiring

function guard(task) {
  return (...args) => task(...args).catch((error) => toast(error.message));
}

$("allocate-form").addEventListener("submit", allocate);
$("lookup-form").addEventListener("submit", lookup);
$("raise-form").addEventListener("submit", raise);
$("filter-form").addEventListener("submit", (event) => {
  event.preventDefault();
  state.page = 1;
  guard(refreshNotifications)();
});
$("page-prev").addEventListener("click", () => { state.page -= 1; guard(refreshNotifications)(); });
$("page-next").addEventListener("click", () => { state.page += 1; guard(refreshNotifications)(); });
$("refresh").addEventListener("click", () => { guard(refreshPool)(); guard(refreshNotifications)(); });

guard(refreshPool)();
guard(refreshNotifications)();
