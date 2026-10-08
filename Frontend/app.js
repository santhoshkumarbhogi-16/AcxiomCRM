const api = async (url, options = {}) => {
  const response = await fetch(`/api${url}`, {
    credentials: "same-origin",
    headers: { ...(options.body ? { "Content-Type": "application/json" } : {}), ...options.headers },
    ...options
  });
  if (response.status === 204) return null;
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.message || `Request failed (${response.status}).`);
  return data;
};

const resources = {
  customers: {
    title: "Customers", singular: "Customer", icon: "◉", search: "Name, email, phone, or company",
    columns: [["customerName", "Customer"], ["companyName", "Company"], ["email", "Email"], ["phone", "Phone"], ["status", "Status"]],
    fields: [
      ["customerName", "Customer name", "text", true], ["email", "Email", "email", true],
      ["phone", "Phone (10 digit)", "tel", true], ["companyName", "Company", "text", true],
      ["address", "Address", "text"], ["city", "City", "text"], ["state", "State", "text"],
      ["status", "Status", "select", true, ["Active", "Inactive"]], ["notes", "Notes", "textarea"]
    ]
  },
  leads: {
    title: "Leads", singular: "Lead", icon: "◇", search: "Name, company, email, or phone",
    columns: [["leadName", "Lead"], ["companyName", "Company"], ["source", "Source"], ["priority", "Priority"], ["status", "Status"]],
    fields: [
      ["leadName", "Lead name", "text", true], ["email", "Email", "email", true],
      ["phone", "Phone (10 digit)", "tel", true], ["companyName", "Company", "text", true],
      ["source", "Source", "select", true, ["Website", "Referral", "Advertisement", "Event", "Other"]],
      ["status", "Status", "select", true, ["New", "Contacted", "Qualified", "Unqualified", "Lost"]],
      ["priority", "Priority", "select", true, ["Low", "Normal", "High"]],
      ["expectedValue", "Expected value", "number", true], ["notes", "Notes", "textarea"]
    ]
  },
  opportunities: {
    title: "Opportunities", singular: "Opportunity", icon: "▣", search: "Opportunity name",
    columns: [["opportunityName", "Opportunity"], ["stage", "Stage"], ["amount", "Amount"], ["probability", "Probability"], ["expectedCloseDate", "Close date"], ["status", "Status"]],
    fields: [
      ["opportunityName", "Opportunity name", "text", true], ["customerId", "Customer", "record:customers"],
      ["leadId", "Lead", "record:leads"], ["amount", "Amount", "number", true],
      ["probability", "Probability (0-100)", "number", true],
      ["stage", "Stage", "select", true, ["Qualification", "Proposal", "Negotiation", "Won", "Lost"]],
      ["expectedCloseDate", "Expected close", "date", true],
      ["status", "Status", "select", true, ["Open", "Closed"]],
      ["source", "Source", "text"], ["notes", "Notes", "textarea"]
    ]
  },
  followups: {
    title: "Follow-ups", singular: "Follow-up", icon: "◷", search: "Subject",
    columns: [["subject", "Subject"], ["followUpType", "Type"], ["followUpDate", "Date"], ["related", "Related record"], ["status", "Status"]],
    fields: [
      ["subject", "Subject", "text", true], ["followUpType", "Type", "select", true, ["Call", "Meeting", "Email", "Task"]],
      ["followUpDate", "Date", "date", true], ["customerId", "Customer", "record:customers"],
      ["leadId", "Lead", "record:leads"], ["opportunityId", "Opportunity", "record:opportunities"],
      ["status", "Status", "select", true, ["Planned", "Completed", "Missed", "Cancelled"]],
      ["remarks", "Notes", "textarea"]
    ]
  },
  activities: {
    title: "Activities", singular: "Activity", icon: "◌", search: "Subject",
    columns: [["subject", "Subject"], ["activityType", "Type"], ["activityDate", "Date"], ["related", "Related record"], ["status", "Status"]],
    fields: [
      ["subject", "Subject", "text", true], ["activityType", "Type", "select", true, ["Call", "Meeting", "Email", "Task"]],
      ["activityDate", "Date and time", "datetime-local"], ["customerId", "Customer", "record:customers"],
      ["leadId", "Lead", "record:leads"], ["opportunityId", "Opportunity", "record:opportunities"],
      ["status", "Status", "select", true, ["Open", "Completed", "Cancelled"]],
      ["description", "Description", "textarea"]
    ]
  }
};

const navItems = [
  ["dashboard", "Overview", "grid"], ["customers", "Customers", "people"], ["leads", "Leads", "lead"],
  ["opportunities", "Opportunities", "briefcase"], ["followups", "Follow-ups", "calendar"],
  ["activities", "Activities", "activity"], ["reports", "Reports", "chart"],
  ["users", "Users", "user"], ["audit", "Audit log", "shield"]
];
const navIcons = {
  grid: '<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>',
  people: '<path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="10" cy="7" r="4"/><path d="M20 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
  lead: '<path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="10" cy="7" r="4"/><path d="M17 8h4m-4 4h4"/>',
  briefcase: '<rect x="3" y="7" width="18" height="14" rx="2"/><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18m-11 0v2h4v-2"/>',
  calendar: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 11h18m6 5 2 2 4-4"/>',
  activity: '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>',
  chart: '<path d="M3 3v18h18"/><path d="m19 9-5 5-4-4-5 5"/><path d="M15 9h4v4"/>',
  user: '<circle cx="12" cy="8" r="4"/><path d="M5 21a7 7 0 0 1 14 0m-2-13 2-2 2 2m-2-2v5"/>',
  shield: '<path d="M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11Z"/><path d="m9 12 2 2 4-4"/>'
};
function navIcon(name) {
  return `<svg class="nav-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">${navIcons[name]}</svg>`;
}
const state = { user: null, page: "dashboard", query: "", status: "", pageNumber: 1, totalPages: 1, currentItems: [], resetUser: "", resetToken: "" };
const $ = selector => document.querySelector(selector);

function showNotice(message, kind = "success") {
  const element = $("#notice");
  element.textContent = message;
  element.className = `notice ${kind}`;
  window.setTimeout(() => element.classList.add("hidden"), 5000);
}

async function initialize() {
  $("#auth-form").addEventListener("submit", submitAuth);
  $("#auth-switch").addEventListener("click", toggleRegistration);
  $("#logout").addEventListener("click", logout);
  $("#mobile-menu").addEventListener("click", () => $("#main-nav").classList.toggle("open"));
  $("#record-form").addEventListener("submit", submitRecord);
  const resetParams = new URLSearchParams(window.location.search);
  if (resetParams.has("resetUser") && resetParams.has("resetToken")) {
    state.resetUser = resetParams.get("resetUser");
    state.resetToken = resetParams.get("resetToken");
    showAuth(false, true);
    return;
  }
  try {
    const result = await api("/auth/me");
    enterApp(result.user);
  } catch {
    showAuth();
  }
}

function showAuth(register = false, reset = false) {
  $("#auth-screen").classList.remove("hidden");
  $("#app-shell").classList.add("hidden");
  $("#name-field").classList.toggle("hidden", !register);
  $("#email-field").classList.toggle("hidden", reset);
  $("#auth-title").textContent = reset ? "Set a new password" : register ? "Create your account" : "Welcome back";
  $("#password-field").firstChild.textContent = reset ? "New password" : "Password";
  $("#password-field input").autocomplete = reset ? "new-password" : "current-password";
  $("#auth-submit").textContent = reset ? "Update password" : register ? "Create account" : "Sign in";
  $("#auth-switch").textContent = reset ? "Return to sign in" : register ? "Already have an account? Sign in" : "Create a Sales Executive account";
  $("#auth-switch").classList.toggle("hidden", reset);
  $("#auth-form").dataset.mode = reset ? "reset" : register ? "register" : "login";
  $("#auth-error").className = "notice error hidden";
}

function toggleRegistration() {
  showAuth($("#auth-form").dataset.mode !== "register");
}

async function submitAuth(event) {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  const mode = event.currentTarget.dataset.mode;
  const register = mode === "register";
  try {
    if (mode === "reset") {
      await api("/auth/password-reset", {
        method: "POST",
        body: JSON.stringify({ userId: state.resetUser, token: state.resetToken, password: form.get("password") })
      });
      state.resetUser = "";
      state.resetToken = "";
      window.history.replaceState({}, "", "/");
      showAuth();
      $("#auth-error").classList.remove("hidden");
      $("#auth-error").className = "notice success";
      $("#auth-error").textContent = "Password updated. You can now sign in.";
      return;
    }
    const result = await api(`/auth/${register ? "register" : "login"}`, {
      method: "POST",
      body: JSON.stringify(Object.fromEntries(form.entries()))
    });
    enterApp(result.user);
  } catch (error) {
    $("#auth-error").textContent = error.message;
    $("#auth-error").classList.remove("hidden");
  }
}

async function logout() {
  try { await api("/auth/logout", { method: "POST" }); } catch { /* session may have expired */ }
  state.user = null;
  showAuth();
}

function enterApp(user) {
  state.user = user;
  $("#auth-screen").classList.add("hidden");
  $("#app-shell").classList.remove("hidden");
  $("#account-name").textContent = user.fullName;
  $("#account-role").textContent = user.role === "SalesExecutive" ? "Sales Executive" : user.role;
  drawNav();
  navigate("dashboard");
}

function drawNav() {
  const available = navItems.filter(([key]) =>
    !["users", "audit"].includes(key) || (key === "users" ? state.user.role === "Admin" : ["Admin", "Manager"].includes(state.user.role)));
  $("#main-nav").innerHTML = available.map(([key, label, icon]) =>
    `<button class="nav-link ${state.page === key ? "active" : ""}" data-page="${key}">${navIcon(icon)}${label}</button>`).join("");
  $("#main-nav").querySelectorAll("[data-page]").forEach(button =>
    button.addEventListener("click", () => navigate(button.dataset.page)));
}

async function navigate(page, pageNumber = 1) {
  state.page = page;
  state.pageNumber = pageNumber;
  state.query = "";
  state.status = "";
  drawNav();
  $("#main-nav").classList.remove("open");
  if (resources[page]) return renderList(page);
  if (page === "dashboard") return renderDashboard();
  if (page === "reports") return renderReports();
  if (page === "users") return renderUsers();
  if (page === "audit") return renderAudit();
}

async function renderDashboard() {
  $("#page").innerHTML = `<div class="page-heading"><div><p class="eyebrow">ACXIOMCRM OVERVIEW</p><h1>Good day, ${escapeHtml(state.user.fullName)}</h1><p class="muted">Your role-scoped sales workspace at a glance.</p></div></div><div id="dashboard-content"><div class="loading">Loading dashboard…</div></div>`;
  try {
    const data = await api("/dashboard");
    const cards = [
      ["Customers", data.totalCustomers], ["Open leads", data.openLeads],
      ["Open opportunities", data.openOpportunities], ["Upcoming follow-ups", data.pendingFollowUps],
      ["Won opportunities", data.wonOpportunities], ["Lost opportunities", data.lostOpportunities]
    ];
    if (data.totalUsers !== undefined) cards.push(["Users", data.totalUsers]);
    $("#dashboard-content").innerHTML = `
      <section class="metrics">${cards.map(([label, value]) => `<article class="metric"><span>${label}</span><strong>${value}</strong></article>`).join("")}</section>
      <section class="dashboard-grid">
        <article class="panel pipeline-panel"><p class="eyebrow">OPEN SALES PIPELINE</p><h2>${money(data.pipelineValue)}</h2><p>Weighted forecast: <strong>${money(data.weightedPipeline)}</strong></p><p>${data.overdueFollowUps} follow-ups overdue</p></article>
        <article class="panel"><h2>Lead status</h2><canvas id="lead-chart"></canvas></article>
        <article class="panel"><h2>Pipeline by stage</h2><canvas id="stage-chart"></canvas></article>
        <article class="panel"><h2>Monthly won sales</h2><canvas id="sales-chart"></canvas></article>
      </section>`;
    makeChart("lead-chart", "doughnut", Object.keys(data.leadStatuses), Object.values(data.leadStatuses), ["#176b87", "#476b86", "#287d70", "#6a91a5", "#35566f", "#86aebc"]);
    makeChart("stage-chart", "bar", Object.keys(data.opportunityStages), Object.values(data.opportunityStages), "#287d90");
    makeChart("sales-chart", "line", data.monthlySales.map(x => x.month), data.monthlySales.map(x => x.amount), "#287d70");
  } catch (error) { $("#dashboard-content").innerHTML = `<div class="notice error">${escapeHtml(error.message)}</div>`; }
}

function makeChart(id, type, labels, values, colors) {
  const canvas = document.getElementById(id);
  if (!canvas || !window.Chart) return;
  const backgroundColor = Array.isArray(colors) ? colors : colors;
  new Chart(canvas, { type, data: { labels, datasets: [{ data: values, backgroundColor, borderColor: Array.isArray(colors) ? colors : colors, fill: type === "line", tension: .3, borderRadius: type === "bar" ? 5 : 0 }] },
    options: { maintainAspectRatio: false, plugins: { legend: { display: type === "doughnut", position: "bottom" } }, scales: type === "doughnut" ? {} : { y: { beginAtZero: true } } } });
}

async function renderList(collection) {
  const config = resources[collection];
  const page = $("#page");
  page.innerHTML = `<div class="page-heading"><div><p class="eyebrow">CRM WORKSPACE</p><h1>${config.title}</h1><p class="muted">Manage ${config.title.toLowerCase()} within your authorized scope.</p></div><button class="button primary" id="create-record">＋ Add ${config.singular.toLowerCase()}</button></div>
    <section class="panel filter-panel"><label>Search<input id="list-search" placeholder="${config.search}" value="${escapeHtml(state.query)}"></label><label>Status<select id="list-status"><option value="">All statuses</option>${statusesFor(collection).map(x => `<option>${x}</option>`).join("")}</select></label><button id="apply-filter" class="button outline">Filter</button><button id="export-csv" class="button outline">Export CSV</button></section>
    <section class="panel table-panel"><div id="record-table"><div class="loading">Loading ${config.title.toLowerCase()}…</div></div></section>`;
  $("#create-record").addEventListener("click", () => openRecordForm(collection));
  $("#apply-filter").addEventListener("click", () => {
    state.query = $("#list-search").value.trim();
    state.status = $("#list-status").value;
    state.pageNumber = 1;
    renderRows(collection);
  });
  $("#export-csv").addEventListener("click", () => exportAll(collection));
  renderRows(collection);
}

function statusesFor(collection) {
  return collection === "customers" ? ["Active", "Inactive"]
    : collection === "leads" ? ["New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"]
      : collection === "opportunities" ? ["Open", "Closed"]
        : collection === "followups" ? ["Planned", "Completed", "Missed", "Cancelled"]
          : ["Open", "Completed", "Cancelled"];
}

async function renderRows(collection) {
  const params = new URLSearchParams({ page: state.pageNumber, pageSize: 25 });
  if (state.query) params.set("q", state.query);
  if (state.status) params.set("status", state.status);
  try {
    const result = await api(`/${collection}?${params}`);
    state.currentItems = result.items;
    state.totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
    const config = resources[collection];
    const rows = result.items.map(item => `<tr>${config.columns.map(([key]) => `<td>${formatCell(item, key)}</td>`).join("")}<td class="actions">${recordActions(collection, item)}</td></tr>`).join("");
    $("#record-table").innerHTML = `<div class="table-wrap"><table><thead><tr>${config.columns.map(([, label]) => `<th>${label}</th>`).join("")}<th>Actions</th></tr></thead><tbody>${rows || `<tr><td colspan="${config.columns.length + 1}" class="empty">No ${config.title.toLowerCase()} found.</td></tr>`}</tbody></table></div>
      <div class="table-footer"><span>${result.totalCount} record(s)</span><div><button class="button small outline" id="previous-page" ${state.pageNumber <= 1 ? "disabled" : ""}>Previous</button><span>Page ${state.pageNumber} of ${state.totalPages}</span><button class="button small outline" id="next-page" ${state.pageNumber >= state.totalPages ? "disabled" : ""}>Next</button></div></div>`;
    $("#previous-page").addEventListener("click", () => { state.pageNumber--; renderRows(collection); });
    $("#next-page").addEventListener("click", () => { state.pageNumber++; renderRows(collection); });
    attachRowActions(collection);
  } catch (error) { $("#record-table").innerHTML = `<div class="notice error">${escapeHtml(error.message)}</div>`; }
}

function recordActions(collection, item) {
  const edit = collection === "leads" ? item.status !== "Converted"
    : collection !== "activities" && collection !== "followups" || ["Open", "Planned"].includes(item.status);
  return `${collection === "leads" && item.status === "Qualified" ? `<button class="text-action convert" data-id="${item._id}">Convert</button>` : ""}
    ${edit ? `<button class="text-action edit" data-id="${item._id}">Edit</button>` : ""}
    <button class="text-action danger delete" data-id="${item._id}">${collection === "customers" ? "Deactivate" : "Delete"}</button>`;
}

function attachRowActions(collection) {
  document.querySelectorAll(".edit").forEach(button => button.addEventListener("click", () => {
    const item = state.currentItems.find(row => row._id === button.dataset.id);
    openRecordForm(collection, item);
  }));
  document.querySelectorAll(".delete").forEach(button => button.addEventListener("click", async () => {
    if (!confirm(collection === "customers" ? "Deactivate this customer?" : "Delete this record?")) return;
    try {
      await api(`/${collection}/${button.dataset.id}`, { method: "DELETE" });
      showNotice(collection === "customers" ? "Customer deactivated." : "Record deleted.");
      renderRows(collection);
    } catch (error) { showNotice(error.message, "error"); }
  }));
  document.querySelectorAll(".convert").forEach(button => button.addEventListener("click", async () => {
    if (!confirm("Convert this qualified lead to a customer and opportunity?")) return;
    try {
      await api(`/leads/${button.dataset.id}/convert`, { method: "POST", body: "{}" });
      showNotice("Lead converted to a customer and opportunity.");
      renderRows(collection);
    } catch (error) { showNotice(error.message, "error"); }
  }));
}

async function openRecordForm(collection, item = null) {
  const config = resources[collection];
  $("#dialog-title").textContent = `${item ? "Edit" : "Add"} ${config.singular.toLowerCase()}`;
  $("#record-form").dataset.collection = collection;
  $("#record-form").dataset.id = item?._id ?? "";
  const fields = config.fields.filter(([key]) => key !== "status" || !(collection === "leads" && item?.status === "Converted"));
  if (["Admin", "Manager"].includes(state.user.role))
    fields.push(["assignedTo", "Assigned Sales Executive", "record:assignees", true]);
  const markup = [];
  for (const [name, label, type, required, options] of fields) {
    const value = item ? (item[name]?._id ?? item[name]) : null;
    if (type.startsWith("record:")) {
      const relatedCollection = type.slice(7);
      const related = await api(relatedCollection === "assignees" ? "/auth/assignees" : `/${relatedCollection}?pageSize=100`);
      markup.push(`<label>${label}<select name="${name}" ${required ? "required" : ""}><option value="">Select ${label.toLowerCase()}</option>${related.items.map(row => `<option value="${row._id}" ${value === row._id ? "selected" : ""}>${escapeHtml(row.fullName ?? row.customerName ?? row.leadName ?? row.opportunityName)}</option>`).join("")}</select></label>`);
    } else if (type === "select") {
      markup.push(`<label>${label}<select name="${name}" ${required ? "required" : ""}><option value="">Select…</option>${options.map(option => `<option ${value === option || (!item && name === "status" && option === (collection === "customers" ? "Active" : collection === "leads" ? "New" : collection === "opportunities" ? "Open" : collection === "followups" ? "Planned" : "Open")) ? "selected" : ""}>${option}</option>`).join("")}</select></label>`);
    } else {
      const normalized = value ? (type === "date" ? new Date(value).toISOString().slice(0, 10) : type === "datetime-local" ? new Date(value).toISOString().slice(0, 16) : value) : "";
      markup.push(`<label class="${type === "textarea" ? "wide" : ""}">${label}${type === "textarea" ? `<textarea name="${name}" ${required ? "required" : ""}>${escapeHtml(normalized)}</textarea>` : `<input name="${name}" type="${type}" value="${escapeHtml(normalized)}" ${required ? "required" : ""} ${type === "number" ? 'step="any"' : ""}>`}</label>`);
    }
  }
  $("#record-fields").innerHTML = markup.join("");
  $("#form-error").classList.add("hidden");
  $("#record-dialog").showModal();
}

async function submitRecord(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const collection = form.dataset.collection;
  const body = Object.fromEntries(new FormData(form).entries());
  for (const key of ["expectedValue", "amount", "probability"]) if (body[key] !== undefined) body[key] = Number(body[key]);
  for (const key of ["customerId", "leadId", "opportunityId"]) if (!body[key]) delete body[key];
  try {
    await api(`/${collection}${form.dataset.id ? `/${form.dataset.id}` : ""}`, {
      method: form.dataset.id ? "PUT" : "POST", body: JSON.stringify(body)
    });
    $("#record-dialog").close();
    showNotice(`${resources[collection].singular} ${form.dataset.id ? "updated" : "created"}.`);
    renderRows(collection);
  } catch (error) {
    $("#form-error").textContent = error.message;
    $("#form-error").classList.remove("hidden");
  }
}

async function exportAll(collection) {
  try {
    const params = new URLSearchParams({ pageSize: 100 });
    if (state.query) params.set("q", state.query);
    if (state.status) params.set("status", state.status);
    const first = await api(`/${collection}?${params}`);
    const records = [...first.items];
    for (let page = 2; page <= Math.ceil(first.totalCount / first.pageSize); page++) {
      params.set("page", page);
      records.push(...(await api(`/${collection}?${params}`)).items);
    }
    const config = resources[collection];
    const columns = config.columns.filter(([key]) => key !== "related");
    const csv = [columns.map(([, title]) => csvEscape(title)).join(","),
      ...records.map(row => columns.map(([key]) => csvEscape(csvRaw(row, key))).join(","))].join("\r\n");
    const link = document.createElement("a");
    link.href = URL.createObjectURL(new Blob(["\ufeff", csv], { type: "text/csv;charset=utf-8" }));
    link.download = `${collection}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  } catch (error) { showNotice(error.message, "error"); }
}

async function renderReports() {
  $("#page").innerHTML = `<div class="page-heading"><div><p class="eyebrow">INSIGHTS</p><h1>Reports</h1><p class="muted">CRM performance in your authorized scope.</p></div></div><div id="report-area" class="loading">Loading reports…</div>`;
  try {
    const [pipeline, conversion] = await Promise.all([api("/reports/pipeline"), api("/reports/conversion")]);
    $("#report-area").innerHTML = `<div class="metrics"><article class="metric"><span>Total leads</span><strong>${conversion.total}</strong></article><article class="metric"><span>Converted</span><strong>${conversion.converted}</strong></article><article class="metric"><span>Conversion rate</span><strong>${conversion.conversionRate.toFixed(1)}%</strong></article><article class="metric"><span>Open pipeline</span><strong>${money(pipeline.totalAmount)}</strong></article></div>
      <div class="dashboard-grid"><article class="panel"><h2>Pipeline by stage</h2>${reportTable(pipeline.byStage)}</article><article class="panel"><h2>Pipeline by owner</h2>${reportTable(pipeline.byOwner)}</article><article class="panel"><h2>Lead conversion by source</h2>${reportTable(conversion.bySource)}</article><article class="panel"><h2>Lead conversion by owner</h2>${reportTable(conversion.byOwner)}</article></div>`;
  } catch (error) { $("#report-area").innerHTML = `<div class="notice error">${escapeHtml(error.message)}</div>`; }
}

async function renderUsers() {
  $("#page").innerHTML = `<div class="page-heading"><div><p class="eyebrow">ADMINISTRATION</p><h1>Users & roles</h1><p class="muted">Manage CRM access and account status.</p></div><button id="create-user" class="button primary">＋ Create user</button></div><section class="panel"><input id="user-search" placeholder="Search name or email"><button id="search-users" class="button outline">Search</button><div id="users-table" class="table-wrap"></div></section>`;
  $("#create-user").addEventListener("click", createUser);
  $("#search-users").addEventListener("click", loadUsers);
  loadUsers();
}

async function loadUsers() {
  try {
    const q = $("#user-search").value;
    const result = await api(`/auth/users?q=${encodeURIComponent(q)}`);
    $("#users-table").innerHTML = `<table><thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>Actions</th></tr></thead><tbody>${result.items.map(user => `<tr><td>${escapeHtml(user.fullName)}</td><td>${escapeHtml(user.email)}</td><td><select data-role="${user._id}">${["Admin", "Manager", "SalesExecutive"].map(role => `<option ${user.role === role ? "selected" : ""}>${role}</option>`).join("")}</select></td><td>${user.isActive ? "Active" : "Inactive"}</td><td><button class="text-action save-user" data-id="${user._id}">Save role</button><button class="text-action toggle-user" data-id="${user._id}" data-active="${user.isActive}">${user.isActive ? "Deactivate" : "Activate"}</button><button class="text-action reset-user" data-id="${user._id}">Issue reset link</button></td></tr>`).join("")}</tbody></table>`;
    document.querySelectorAll(".save-user").forEach(button => button.addEventListener("click", async () => {
      const role = document.querySelector(`[data-role="${button.dataset.id}"]`).value;
      try { await api(`/auth/users/${button.dataset.id}`, { method: "PATCH", body: JSON.stringify({ role }) }); showNotice("User role updated."); }
      catch (error) { showNotice(error.message, "error"); }
    }));
    document.querySelectorAll(".toggle-user").forEach(button => button.addEventListener("click", async () => {
      try { await api(`/auth/users/${button.dataset.id}`, { method: "PATCH", body: JSON.stringify({ isActive: button.dataset.active !== "true" }) }); loadUsers(); }
      catch (error) { showNotice(error.message, "error"); }
    }));
    document.querySelectorAll(".reset-user").forEach(button => button.addEventListener("click", async () => {
      try {
        const result = await api(`/auth/users/${button.dataset.id}/reset-link`, { method: "POST", body: "{}" });
        prompt("Share this one-hour password reset link through a trusted channel:", new URL(result.resetUrl, window.location.origin).href);
      } catch (error) { showNotice(error.message, "error"); }
    }));
  } catch (error) { $("#users-table").innerHTML = `<div class="notice error">${escapeHtml(error.message)}</div>`; }
}

async function createUser() {
  const fullName = prompt("Full name");
  if (!fullName) return;
  const email = prompt("Email address");
  if (!email) return;
  const password = prompt("Temporary password (must include upper/lowercase, number and symbol)");
  if (!password) return;
  const role = prompt("Role: Admin, Manager, or SalesExecutive", "SalesExecutive");
  try {
    await api("/auth/users", { method: "POST", body: JSON.stringify({ fullName, email, password, role }) });
    showNotice("User created.");
    loadUsers();
  } catch (error) { showNotice(error.message, "error"); }
}

async function renderAudit() {
  $("#page").innerHTML = `<div class="page-heading"><div><p class="eyebrow">SECURITY & COMPLIANCE</p><h1>Audit log</h1><p class="muted">Recent authentication and CRM changes.</p></div></div><section class="panel" id="audit-panel">Loading audit events…</section>`;
  try {
    const result = await api("/reports/audit");
    $("#audit-panel").innerHTML = `<div class="table-wrap"><table><thead><tr><th>Date</th><th>User</th><th>Action</th><th>Module</th><th>Record</th><th>Details</th></tr></thead><tbody>${result.items.map(item => `<tr><td>${new Date(item.createdAt).toLocaleString()}</td><td>${escapeHtml(item.userId?.fullName ?? "System")}</td><td>${escapeHtml(item.action)}</td><td>${escapeHtml(item.entityName)}</td><td>${escapeHtml(item.recordId)}</td><td>${escapeHtml([item.oldValue, item.newValue].filter(Boolean).join(" → "))}</td></tr>`).join("")}</tbody></table></div>`;
  } catch (error) { $("#audit-panel").innerHTML = `<div class="notice error">${escapeHtml(error.message)}</div>`; }
}

function formatCell(item, key) {
  if (key === "related") {
    const kind = item.customerId ? "Customer" : item.leadId ? "Lead" : "Opportunity";
    return `${kind} #${String(item.customerId ?? item.leadId ?? item.opportunityId).slice(-6)}`;
  }
  if (key === "amount") return money(item.amount);
  if (key === "expectedCloseDate" || key === "followUpDate" || key === "activityDate")
    return item[key] ? new Date(item[key]).toLocaleDateString() : "—";
  return escapeHtml(item[key] ?? "—");
}
function csvRaw(item, key) {
  if (key === "amount") return item.amount;
  if (key === "expectedCloseDate" || key === "followUpDate" || key === "activityDate")
    return item[key] ? new Date(item[key]).toISOString() : "";
  return item[key] ?? "";
}
function csvEscape(value) {
  let safe = String(value ?? "");
  if (/^[=+\-@\t\r]/.test(safe)) safe = `'${safe}`;
  return `"${safe.replaceAll('"', '""')}"`;
}
function reportTable(items = []) {
  return `<div class="table-wrap"><table><thead><tr><th>Group</th><th>Total</th><th>Converted/Amount</th><th>Rate/Weighted</th></tr></thead><tbody>${items.map(row => `<tr><td>${escapeHtml(row.name)}</td><td>${row.total ?? row.count ?? 0}</td><td>${row.converted ?? money(row.amount)}</td><td>${row.rate !== undefined ? `${row.rate.toFixed(1)}%` : money(row.weightedAmount)}</td></tr>`).join("") || `<tr><td colspan="4" class="empty">No data.</td></tr>`}</tbody></table></div>`;
}
function money(value) { return `₹ ${Number(value || 0).toLocaleString("en-IN", { maximumFractionDigits: 2 })}`; }
function escapeHtml(value) { return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]); }

initialize();
