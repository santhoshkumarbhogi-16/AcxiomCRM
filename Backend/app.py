from __future__ import annotations

import hashlib
import hmac
import os
import re
import secrets
import sqlite3
import threading
import uuid
from datetime import date, datetime, timedelta, timezone
from functools import wraps
from pathlib import Path
from typing import Any

from flask import Flask, current_app, g, jsonify, make_response, request, send_from_directory
from itsdangerous import BadSignature, URLSafeTimedSerializer
from werkzeug.security import check_password_hash, generate_password_hash

PROJECT_ROOT = Path(__file__).resolve().parents[1]
FRONTEND_DIRECTORY = PROJECT_ROOT / "Frontend"
DATABASE_DIRECTORY = PROJECT_ROOT / "Database"


def load_local_env() -> None:
    env_path = Path(__file__).resolve().parent / ".env"
    if not env_path.is_file():
        return
    for line in env_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        name, value = line.split("=", 1)
        os.environ.setdefault(name.strip(), value.strip().strip("\"'"))


load_local_env()

COLLECTIONS: dict[str, dict[str, Any]] = {
    "customers": {
        "field": "customerName",
        "search": ["customerName", "email", "phone", "companyName"],
        "sort": "createdAt",
    },
    "leads": {
        "field": "leadName",
        "search": ["leadName", "companyName", "email", "phone"],
        "sort": "createdAt",
    },
    "opportunities": {
        "field": "opportunityName",
        "search": ["opportunityName"],
        "sort": "createdAt",
    },
    "followups": {"field": "subject", "search": ["subject"], "sort": "followUpDate"},
    "activities": {"field": "subject", "search": ["subject"], "sort": "activityDate"},
}
LEAD_TRANSITIONS = {
    "New": {"Contacted", "Qualified", "Unqualified", "Lost"},
    "Contacted": {"Qualified", "Unqualified", "Lost"},
    "Qualified": {"Unqualified", "Lost"},
    "Unqualified": {"New"},
    "Converted": set(),
    "Lost": {"New"},
}

SCHEMA = """
CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    fullName TEXT NOT NULL CHECK(length(fullName) BETWEEN 1 AND 100),
    email TEXT NOT NULL COLLATE NOCASE UNIQUE,
    passwordHash TEXT NOT NULL,
    role TEXT NOT NULL CHECK(role IN ('Admin', 'Manager', 'SalesExecutive')),
    isActive INTEGER NOT NULL DEFAULT 1 CHECK(isActive IN (0, 1)),
    lockoutUntil TEXT,
    failedLoginCount INTEGER NOT NULL DEFAULT 0,
    resetTokenHash TEXT,
    resetTokenExpiresAt TEXT,
    createdAt TEXT NOT NULL,
    updatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS customers (
    id TEXT PRIMARY KEY, customerCode TEXT NOT NULL UNIQUE, customerName TEXT NOT NULL,
    email TEXT NOT NULL COLLATE NOCASE UNIQUE, phone TEXT NOT NULL UNIQUE,
    companyName TEXT NOT NULL, address TEXT NOT NULL DEFAULT '', city TEXT NOT NULL DEFAULT '',
    state TEXT NOT NULL DEFAULT '', notes TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active', 'Inactive')),
    createdBy TEXT REFERENCES users(id) ON DELETE SET NULL,
    assignedTo TEXT REFERENCES users(id) ON DELETE SET NULL,
    createdAt TEXT NOT NULL, updatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS leads (
    id TEXT PRIMARY KEY, leadCode TEXT NOT NULL UNIQUE, leadName TEXT NOT NULL,
    email TEXT NOT NULL COLLATE NOCASE, phone TEXT NOT NULL, companyName TEXT NOT NULL,
    source TEXT NOT NULL CHECK(source IN ('Website', 'Referral', 'Advertisement', 'Event', 'Other')),
    status TEXT NOT NULL DEFAULT 'New' CHECK(status IN ('New', 'Contacted', 'Qualified', 'Unqualified', 'Converted', 'Lost')),
    priority TEXT NOT NULL DEFAULT 'Normal' CHECK(priority IN ('Low', 'Normal', 'High')),
    expectedValue REAL NOT NULL DEFAULT 0, notes TEXT NOT NULL DEFAULT '',
    assignedTo TEXT REFERENCES users(id) ON DELETE SET NULL,
    createdAt TEXT NOT NULL, updatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS opportunities (
    id TEXT PRIMARY KEY, opportunityName TEXT NOT NULL,
    customerId TEXT REFERENCES customers(id) ON DELETE SET NULL,
    leadId TEXT REFERENCES leads(id) ON DELETE SET NULL,
    amount REAL NOT NULL CHECK(amount > 0), probability INTEGER NOT NULL CHECK(probability BETWEEN 0 AND 100),
    stage TEXT NOT NULL CHECK(stage IN ('Qualification', 'Proposal', 'Negotiation', 'Won', 'Lost')),
    expectedCloseDate TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Open' CHECK(status IN ('Open', 'Closed')),
    source TEXT NOT NULL DEFAULT '', notes TEXT NOT NULL DEFAULT '',
    assignedTo TEXT REFERENCES users(id) ON DELETE SET NULL,
    createdAt TEXT NOT NULL, updatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS followups (
    id TEXT PRIMARY KEY,
    customerId TEXT REFERENCES customers(id) ON DELETE SET NULL,
    leadId TEXT REFERENCES leads(id) ON DELETE SET NULL,
    opportunityId TEXT REFERENCES opportunities(id) ON DELETE SET NULL,
    subject TEXT NOT NULL, followUpType TEXT NOT NULL CHECK(followUpType IN ('Call', 'Meeting', 'Email', 'Task')),
    followUpDate TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Planned'
      CHECK(status IN ('Planned', 'Completed', 'Missed', 'Cancelled')),
    remarks TEXT NOT NULL DEFAULT '', assignedTo TEXT REFERENCES users(id) ON DELETE SET NULL,
    createdAt TEXT NOT NULL, updatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS activities (
    id TEXT PRIMARY KEY,
    customerId TEXT REFERENCES customers(id) ON DELETE SET NULL,
    leadId TEXT REFERENCES leads(id) ON DELETE SET NULL,
    opportunityId TEXT REFERENCES opportunities(id) ON DELETE SET NULL,
    activityType TEXT NOT NULL CHECK(activityType IN ('Call', 'Meeting', 'Email', 'Task')),
    subject TEXT NOT NULL, description TEXT NOT NULL DEFAULT '',
    activityDate TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Open'
      CHECK(status IN ('Open', 'Completed', 'Cancelled')),
    assignedTo TEXT REFERENCES users(id) ON DELETE SET NULL,
    createdAt TEXT NOT NULL, updatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS auditLogs (
    id TEXT PRIMARY KEY, userId TEXT REFERENCES users(id) ON DELETE SET NULL,
    action TEXT NOT NULL, entityName TEXT NOT NULL DEFAULT '', recordId TEXT NOT NULL DEFAULT '',
    oldValue TEXT NOT NULL DEFAULT '', newValue TEXT NOT NULL DEFAULT '',
    ipAddress TEXT NOT NULL DEFAULT '', createdAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_customers_assigned ON customers(assignedTo);
CREATE INDEX IF NOT EXISTS idx_leads_assigned ON leads(assignedTo);
CREATE INDEX IF NOT EXISTS idx_opportunities_assigned ON opportunities(assignedTo);
CREATE INDEX IF NOT EXISTS idx_followups_assigned ON followups(assignedTo);
CREATE INDEX IF NOT EXISTS idx_activities_assigned ON activities(assignedTo);
CREATE INDEX IF NOT EXISTS idx_audit_created ON auditLogs(createdAt);
CREATE TRIGGER IF NOT EXISTS auditLogs_no_update BEFORE UPDATE ON auditLogs
BEGIN SELECT RAISE(ABORT, 'Audit entries are append-only'); END;
CREATE TRIGGER IF NOT EXISTS auditLogs_no_delete BEFORE DELETE ON auditLogs
BEGIN SELECT RAISE(ABORT, 'Audit entries are append-only'); END;
"""


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def iso_now() -> str:
    return utc_now().isoformat(timespec="milliseconds").replace("+00:00", "Z")


def new_id() -> str:
    return str(uuid.uuid4())


def connect_db(database_path: str) -> sqlite3.Connection:
    connection = sqlite3.connect(database_path, timeout=10, isolation_level=None)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON")
    connection.execute("PRAGMA journal_mode = WAL")
    return connection


def get_db() -> sqlite3.Connection:
    if "db" not in g:
        g.db = connect_db(str(getattr(g, "_database_path", current_app.config["DATABASE"])))
    return g.db


def row_dict(row: sqlite3.Row | None) -> dict[str, Any] | None:
    return dict(row) if row is not None else None


def insert_row(table: str, values: dict[str, Any]) -> dict[str, Any]:
    connection = get_db()
    now = iso_now()
    row = {**values, "id": values.get("id") or new_id(), "createdAt": values.get("createdAt", now)}
    if table != "auditLogs":
        row["updatedAt"] = values.get("updatedAt", now)
    columns = list(row)
    connection.execute(
        f"INSERT INTO {table} ({', '.join(columns)}) VALUES ({', '.join('?' for _ in columns)})",
        [row[column] for column in columns],
    )
    return row_dict(connection.execute(f"SELECT * FROM {table} WHERE id = ?", (row["id"],)).fetchone()) or {}


def update_row(table: str, record_id: str, values: dict[str, Any]) -> dict[str, Any]:
    if values:
        columns = list(values)
        assignments = ", ".join(f"{column} = ?" for column in columns)
        get_db().execute(
            f"UPDATE {table} SET {assignments}, updatedAt = ? WHERE id = ?",
            [values[column] for column in columns] + [iso_now(), record_id],
        )
    return row_dict(get_db().execute(f"SELECT * FROM {table} WHERE id = ?", (record_id,)).fetchone()) or {}


def public_user(user: dict[str, Any]) -> dict[str, Any]:
    return {
        "id": user["id"],
        "_id": user["id"],
        "fullName": user["fullName"],
        "email": user["email"],
        "role": user["role"],
        "isActive": bool(user["isActive"]),
    }


def public_record(row: dict[str, Any] | sqlite3.Row) -> dict[str, Any]:
    result = dict(row)
    result["_id"] = result["id"]
    if "isActive" in result:
        result["isActive"] = bool(result["isActive"])
    if "assignedId" in result:
        result["assignedTo"] = (
            {
                "_id": result["assignedId"],
                "fullName": result["assignedFullName"],
                "email": result["assignedEmail"],
            }
            if result["assignedId"]
            else None
        )
        for key in ("assignedId", "assignedFullName", "assignedEmail"):
            result.pop(key, None)
    return result


def create_app(test_config: dict[str, Any] | None = None) -> Flask:
    app = Flask(__name__, static_folder=None)
    default_database = DATABASE_DIRECTORY / "acxiomcrm-python.sqlite"
    app.config.update(
        DATABASE=os.environ.get("DATABASE_PATH", str(default_database)),
        JWT_SECRET=os.environ.get("JWT_SECRET", ""),
        SESSION_COOKIE_NAME="acxiom_session",
        SESSION_COOKIE_HTTPONLY=True,
        SESSION_COOKIE_SAMESITE="Strict",
        SESSION_COOKIE_SECURE=os.environ.get("NODE_ENV") == "production",
        MAX_CONTENT_LENGTH=100 * 1024,
    )
    if test_config:
        app.config.update(test_config)
    Path(app.config["DATABASE"]).parent.mkdir(parents=True, exist_ok=True)
    initialize_database(app.config["DATABASE"])
    app.json.sort_keys = False
    register_routes(app)
    login_attempts: dict[str, list[float]] = {}
    login_attempts_lock = threading.Lock()

    @app.before_request
    def limit_login_attempts():
        g._database_path = app.config["DATABASE"]
        if request.path != "/api/auth/login" or request.method != "POST":
            return None
        now = datetime.now().timestamp()
        key = request.remote_addr or "unknown"
        with login_attempts_lock:
            recent = [attempt for attempt in login_attempts.get(key, []) if now - attempt < 60]
            if len(recent) >= 10:
                return jsonify(message="Too many login attempts. Try again later."), 429
            recent.append(now)
            login_attempts[key] = recent
        return None

    @app.after_request
    def set_security_headers(response: Any) -> Any:
        response.headers.setdefault("X-Content-Type-Options", "nosniff")
        response.headers.setdefault("X-Frame-Options", "DENY")
        response.headers.setdefault("Referrer-Policy", "strict-origin-when-cross-origin")
        response.headers.setdefault(
            "Content-Security-Policy",
            "default-src 'self'; script-src 'self' https://cdn.jsdelivr.net; "
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; "
            "font-src 'self' https://cdn.jsdelivr.net data:; img-src 'self' data:; connect-src 'self'",
        )
        return response

    @app.teardown_appcontext
    def close_database(error: BaseException | None = None) -> None:
        if error is not None:
            app.logger.error("Request ended with an error while closing its database connection.")
        connection = g.pop("db", None)
        if connection is not None:
            connection.close()

    return app


def initialize_database(database_path: str) -> None:
    DATABASE_DIRECTORY.mkdir(parents=True, exist_ok=True)
    connection = connect_db(database_path)
    try:
        connection.executescript(SCHEMA)
        connection.commit()
    finally:
        connection.close()


def register_routes(app: Flask) -> None:
    def db() -> sqlite3.Connection:
        return get_db()

    def fail(message: str, status: int = 400):
        return jsonify(message=message), status

    def log_audit(action: str, entity: str = "", record_id: str = "", old: str = "", new: str = "") -> None:
        insert_row(
            "auditLogs",
            {
                "userId": getattr(g, "user", {}).get("_id"),
                "action": action,
                "entityName": entity,
                "recordId": str(record_id or ""),
                "oldValue": str(old or ""),
                "newValue": str(new or ""),
                "ipAddress": request.remote_addr or "",
            },
        )

    def login_required(view):
        @wraps(view)
        def wrapped(*args, **kwargs):
            token = request.cookies.get(app.config["SESSION_COOKIE_NAME"])
            if not token:
                return fail("Sign in to continue.", 401)
            try:
                user_id = serializer().loads(token, max_age=8 * 60 * 60)
            except BadSignature:
                return fail("Your session has expired. Sign in again.", 401)
            row = db().execute(
                "SELECT id, fullName, email, role, isActive FROM users WHERE id = ?",
                (user_id,),
            ).fetchone()
            if row is None or not row["isActive"]:
                response = make_response(fail("This account is unavailable.", 401))
                response.delete_cookie(app.config["SESSION_COOKIE_NAME"], path="/", samesite="Strict")
                return response
            g.user = {**dict(row), "_id": row["id"], "isActive": bool(row["isActive"])}
            return view(*args, **kwargs)

        return wrapped

    def roles_required(*roles):
        def decorator(view):
            @wraps(view)
            @login_required
            def wrapped(*args, **kwargs):
                if g.user["role"] not in roles:
                    return fail("You do not have permission to perform this action.", 403)
                return view(*args, **kwargs)

            return wrapped

        return decorator

    def serializer() -> URLSafeTimedSerializer:
        return URLSafeTimedSerializer(app.config["JWT_SECRET"], salt="acxiomcrm-session")

    def record_scope(alias: str = "") -> tuple[str, list[Any]]:
        if g.user["role"] in {"Admin", "Manager"}:
            return "1 = 1", []
        return f"{alias}assignedTo = ?", [g.user["_id"]]

    def valid_id(value: Any) -> bool:
        try:
            uuid.UUID(str(value))
            return True
        except (ValueError, TypeError, AttributeError):
            return False

    def parse_date(value: Any) -> datetime | None:
        if isinstance(value, datetime):
            return value
        if not isinstance(value, str) or not value:
            return None
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
            return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
        except ValueError:
            try:
                return datetime.combine(date.fromisoformat(value), datetime.min.time(), timezone.utc)
            except ValueError:
                return None

    def date_string(value: Any) -> str | None:
        if isinstance(value, datetime):
            return value.astimezone(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
        if isinstance(value, date):
            return value.isoformat()
        return value

    def normalize_values(data: dict[str, Any]) -> dict[str, Any]:
        return {key: date_string(value) for key, value in data.items()}

    def is_before_today(value: Any) -> bool:
        parsed = parse_date(value)
        return parsed is not None and parsed.date() < date.today()

    def escape_like(value: str) -> str:
        return re.sub(r"([\\%_])", r"\\\1", value)

    def make_code(prefix: str) -> str:
        return f"{prefix}-{uuid.uuid4().hex[:10].upper()}"

    @app.errorhandler(sqlite3.IntegrityError)
    def handle_integrity_error(error: sqlite3.IntegrityError):
        message = str(error)
        if "UNIQUE constraint failed" in message or "PRIMARY KEY" in message:
            return fail("A record with that unique value already exists.", 409)
        app.logger.exception("SQLite integrity constraint failed", exc_info=error)
        return fail("The record violates a database constraint.", 400)

    @app.errorhandler(413)
    def handle_too_large(error):
        app.logger.info("Rejected oversized request: %s", error)
        return fail("Request body exceeds the 100 KB limit.", 413)

    @app.get("/api/health")
    def health():
        return jsonify(status="ok", database="sqlite")

    @app.post("/api/auth/login")
    def login():
        data = request.get_json(silent=True) or {}
        email = str(data.get("email", "")).strip().lower()
        password = data.get("password")
        if len(email) > 150 or not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", email) or not isinstance(password, str) or not password:
            return fail("Enter a valid email and password.")
        user = db().execute("SELECT * FROM users WHERE email = ? COLLATE NOCASE", (email,)).fetchone()
        if user is None or not user["isActive"]:
            log_audit("LoginFailed", "Authentication", new="Invalid credentials or inactive account.")
            return fail("Invalid credentials or inactive account.", 401)
        if user["lockoutUntil"] and user["lockoutUntil"] > iso_now():
            log_audit("LoginFailed", "Authentication", user["id"], new="Account is locked.")
            return fail("Account locked temporarily. Try again later.", 423)
        if not check_password_hash(user["passwordHash"], password):
            failures = int(user["failedLoginCount"]) + 1
            locked = failures >= 5
            update_row(
                "users",
                user["id"],
                {
                    "failedLoginCount": 0 if locked else failures,
                    "lockoutUntil": (utc_now() + timedelta(minutes=10)).isoformat().replace("+00:00", "Z") if locked else None,
                },
            )
            log_audit("AccountLocked" if locked else "LoginFailed", "Authentication", user["id"])
            return fail("Account locked temporarily." if locked else "Invalid credentials.", 423 if locked else 401)
        update_row("users", user["id"], {"failedLoginCount": 0, "lockoutUntil": None})
        response = jsonify(user=public_user(dict(user)))
        response.set_cookie(
            app.config["SESSION_COOKIE_NAME"],
            serializer().dumps(user["id"]),
            max_age=8 * 60 * 60,
            httponly=True,
            secure=app.config["SESSION_COOKIE_SECURE"],
            samesite="Strict",
            path="/",
        )
        g.user = {**dict(user), "_id": user["id"]}
        log_audit("LoginSucceeded", "Authentication", user["id"])
        return response

    @app.post("/api/auth/register")
    def register():
        data = request.get_json(silent=True) or {}
        name = str(data.get("fullName", "")).strip()
        email = str(data.get("email", "")).strip().lower()
        password = data.get("password")
        if not name or len(name) > 100 or len(email) > 150 or not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", email) or not isinstance(password, str) or len(password) < 8:
            return fail("Provide a name, valid email, and password of at least 8 characters.")
        if not password_is_strong(password):
            return fail("Password must include uppercase, lowercase, numeric, and special characters.")
        user = insert_row(
            "users",
            {"fullName": name, "email": email, "passwordHash": generate_password_hash(password), "role": "SalesExecutive"},
        )
        g.user = {"_id": user["id"]}
        log_audit("Register", "User", user["id"], new="SalesExecutive")
        response = jsonify(user=public_user(user))
        response.status_code = 201
        response.set_cookie(
            app.config["SESSION_COOKIE_NAME"], serializer().dumps(user["id"]),
            max_age=8 * 60 * 60, httponly=True,
            secure=app.config["SESSION_COOKIE_SECURE"], samesite="Strict", path="/",
        )
        log_audit("LoginSucceeded", "Authentication", user["id"])
        return response

    @app.post("/api/auth/password-reset")
    def password_reset():
        data = request.get_json(silent=True) or {}
        user_id, token, password = data.get("userId"), data.get("token"), data.get("password")
        if not valid_id(user_id) or not isinstance(token, str) or not isinstance(password, str) or len(password) < 8:
            return fail("Provide a valid reset link and a strong password.")
        if not password_is_strong(password):
            return fail("Provide a valid reset link and a strong password.")
        user = db().execute("SELECT * FROM users WHERE id = ? AND resetTokenExpiresAt > ?", (user_id, iso_now())).fetchone()
        token_hash = hashlib.sha256(token.encode()).hexdigest()
        if user is None or not user["resetTokenHash"] or not hmac.compare_digest(user["resetTokenHash"], token_hash):
            return fail("This password reset link is invalid or expired.")
        update_row(
            "users", user_id,
            {"passwordHash": generate_password_hash(password), "resetTokenHash": None,
             "resetTokenExpiresAt": None, "failedLoginCount": 0, "lockoutUntil": None},
        )
        log_audit("PasswordReset", "User", user_id)
        return "", 204

    @app.post("/api/auth/logout")
    @login_required
    def logout():
        log_audit("Logout", "Authentication", g.user["_id"])
        response = make_response("", 204)
        response.delete_cookie(app.config["SESSION_COOKIE_NAME"], path="/", samesite="Strict")
        return response

    @app.get("/api/auth/me")
    @login_required
    def current_user():
        return jsonify(user=public_user(g.user))

    @app.get("/api/auth/users")
    @roles_required("Admin")
    def list_users():
        page, page_size = pagination()
        query = str(request.args.get("q", "")).strip()
        where, params = ("WHERE fullName LIKE ? ESCAPE '\\' OR email LIKE ? ESCAPE '\\'", [f"%{escape_like(query)}%"] * 2) if query else ("", [])
        items = db().execute(
            f"SELECT id, fullName, email, role, isActive, lockoutUntil, createdAt FROM users {where} ORDER BY fullName COLLATE NOCASE LIMIT ? OFFSET ?",
            [*params, page_size, (page - 1) * page_size],
        ).fetchall()
        count = db().execute(f"SELECT COUNT(*) AS count FROM users {where}", params).fetchone()["count"]
        return jsonify(items=[public_user(dict(row)) for row in items], page=page, pageSize=page_size, totalCount=count)

    @app.get("/api/auth/assignees")
    @roles_required("Admin", "Manager")
    def list_assignees():
        rows = db().execute(
            "SELECT id, fullName FROM users WHERE role = 'SalesExecutive' AND isActive = 1 ORDER BY fullName COLLATE NOCASE"
        ).fetchall()
        return jsonify(items=[{"_id": row["id"], **dict(row)} for row in rows])

    @app.post("/api/auth/users")
    @roles_required("Admin")
    def create_user():
        data = request.get_json(silent=True) or {}
        name = str(data.get("fullName", "")).strip()
        email = str(data.get("email", "")).strip().lower()
        password = data.get("password")
        role = data.get("role")
        if not name or len(name) > 100 or len(email) > 150 or not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", email) or not isinstance(password, str) or len(password) < 8 or role not in {"Admin", "Manager", "SalesExecutive"}:
            return fail("Check the name, email, password, and role.")
        if not password_is_strong(password):
            return fail("Password must include uppercase, lowercase, numeric, and special characters.")
        created = insert_row(
            "users",
            {"fullName": name, "email": email, "passwordHash": generate_password_hash(password), "role": role},
        )
        log_audit("Create", "User", created["id"], new=role)
        return jsonify(user=public_user(created)), 201

    @app.patch("/api/auth/users/<user_id>")
    @roles_required("Admin")
    def update_user(user_id: str):
        if not valid_id(user_id):
            return fail("Invalid user ID.")
        data = request.get_json(silent=True) or {}
        if not (data.get("role") in {"Admin", "Manager", "SalesExecutive"} or isinstance(data.get("isActive"), bool) or data.get("unlock") is True):
            return fail("Provide a valid role, account status, or unlock request.")
        user = db().execute("SELECT * FROM users WHERE id = ?", (user_id,)).fetchone()
        if user is None:
            return fail("User not found.", 404)
        if user_id == g.user["_id"] and (data.get("role") and data["role"] != "Admin" or data.get("isActive") is False):
            return fail("You cannot demote or deactivate your own administrator account.")
        if user["role"] == "Admin" and data.get("role") and data["role"] != "Admin":
            count = db().execute("SELECT COUNT(*) AS count FROM users WHERE role = 'Admin' AND isActive = 1").fetchone()["count"]
            if count <= 1:
                return fail("The last active administrator cannot be demoted.")
        updates: dict[str, Any] = {}
        if data.get("role"):
            updates["role"] = data["role"]
        if isinstance(data.get("isActive"), bool):
            updates["isActive"] = int(data["isActive"])
        if data.get("unlock") is True:
            updates.update(lockoutUntil=None, failedLoginCount=0)
        old_value = f"{user['role']}; active={bool(user['isActive'])}"
        updated = update_row("users", user_id, updates)
        log_audit("Update", "User", user_id, old_value, f"{updated['role']}; active={bool(updated['isActive'])}")
        return jsonify(user=public_user(updated))

    @app.post("/api/auth/users/<user_id>/reset-link")
    @roles_required("Admin")
    def issue_reset_link(user_id: str):
        if not valid_id(user_id):
            return fail("Invalid user ID.")
        user = db().execute("SELECT id FROM users WHERE id = ?", (user_id,)).fetchone()
        if user is None:
            return fail("User not found.", 404)
        token = secrets.token_urlsafe(32)
        expires_at = (utc_now() + timedelta(hours=1)).isoformat().replace("+00:00", "Z")
        update_row("users", user_id, {"resetTokenHash": hashlib.sha256(token.encode()).hexdigest(), "resetTokenExpiresAt": expires_at})
        log_audit("PasswordResetLinkIssued", "User", user_id)
        return jsonify(resetUrl=f"/?resetUser={user_id}&resetToken={token}", expiresAt=expires_at)

    @app.get("/api/dashboard")
    @login_required
    def dashboard():
        scope, params = record_scope()
        condition = "" if scope == "1 = 1" else f"WHERE {scope}"
        customers = db().execute(f"SELECT COUNT(*) AS count FROM customers {condition}", params).fetchone()["count"]
        leads = db().execute(f"SELECT status FROM leads {condition}", params).fetchall()
        opportunities = db().execute(
            f"SELECT amount, probability, stage, status, expectedCloseDate FROM opportunities {condition}", params
        ).fetchall()
        followup_condition = f"{condition} {'AND' if condition else 'WHERE'} status = 'Planned'"
        followups = db().execute(f"SELECT followUpDate FROM followups {followup_condition}", params).fetchall()
        today = date.today()
        open_items = [dict(row) for row in opportunities if row["status"] == "Open"]
        won = [dict(row) for row in opportunities if row["stage"] == "Won"]
        statuses: dict[str, int] = {}
        for lead in leads:
            statuses[lead["status"]] = statuses.get(lead["status"], 0) + 1
        stages: dict[str, float] = {}
        for item in open_items:
            stages[item["stage"]] = stages.get(item["stage"], 0) + item["amount"]
        monthly_sales = []
        month_index = today.year * 12 + today.month - 1
        for offset in range(5, -1, -1):
            year, month_number = divmod(month_index - offset, 12)
            month = date(year, month_number + 1, 1)
            amount = sum(
                row["amount"] for row in won
                if (parsed := parse_date(row["expectedCloseDate"])) and parsed.year == month.year and parsed.month == month.month
            )
            monthly_sales.append({"month": month.strftime("%b %y"), "amount": amount})
        result = {
            "totalCustomers": customers,
            "totalLeads": len(leads),
            "openLeads": sum(row["status"] not in {"Lost", "Converted", "Unqualified"} for row in leads),
            "leadStatuses": statuses,
            "totalOpportunities": len(opportunities),
            "openOpportunities": len(open_items),
            "wonOpportunities": sum(row["stage"] == "Won" for row in opportunities),
            "lostOpportunities": sum(row["stage"] == "Lost" for row in opportunities),
            "pipelineValue": sum(row["amount"] for row in open_items),
            "weightedPipeline": sum(row["amount"] * row["probability"] / 100 for row in open_items),
            "pendingFollowUps": sum(bool(parse_date(row["followUpDate"])) and parse_date(row["followUpDate"]).date() >= today for row in followups),
            "overdueFollowUps": sum(bool(parse_date(row["followUpDate"])) and parse_date(row["followUpDate"]).date() < today for row in followups),
            "opportunityStages": stages,
            "monthlySales": monthly_sales,
        }
        if g.user["role"] == "Admin":
            result["totalUsers"] = db().execute("SELECT COUNT(*) AS count FROM users").fetchone()["count"]
        return jsonify(result)

    @app.get("/api/reports/user-activity")
    @roles_required("Admin", "Manager")
    def user_activity():
        rows = db().execute(
            """SELECT a.userId, a.action, u.fullName, COUNT(*) AS count, MAX(a.createdAt) AS latest
            FROM auditLogs a LEFT JOIN users u ON u.id = a.userId
            GROUP BY a.userId, a.action, u.fullName ORDER BY count DESC LIMIT 500"""
        ).fetchall()
        return jsonify(items=[dict(row) for row in rows])

    @app.get("/api/docs")
    def api_docs():
        paths = {
            "/api/health": ["get"],
            "/api/auth/login": ["post"],
            "/api/auth/register": ["post"],
            "/api/auth/logout": ["post"],
            "/api/auth/me": ["get"],
            "/api/auth/users": ["get", "post"],
            "/api/dashboard": ["get"],
            "/api/customers": ["get", "post"],
            "/api/customers/{id}": ["get", "put", "delete"],
            "/api/leads": ["get", "post"],
            "/api/leads/{id}": ["get", "put", "delete"],
            "/api/leads/{id}/convert": ["post"],
            "/api/opportunities": ["get", "post"],
            "/api/opportunities/{id}": ["get", "put", "delete"],
            "/api/followups": ["get", "post"],
            "/api/followups/{id}": ["get", "put", "delete"],
            "/api/activities": ["get", "post"],
            "/api/activities/{id}": ["get", "put", "delete"],
            "/api/reports/pipeline": ["get"],
            "/api/reports/conversion": ["get"],
            "/api/reports/audit": ["get"],
            "/api/reports/user-activity": ["get"],
        }
        return jsonify(
            openapi="3.0.3",
            info={"title": "AcxiomCRM API", "version": "1.0.0"},
            components={"securitySchemes": {"cookieAuth": {"type": "apiKey", "in": "cookie", "name": "acxiom_session"}}},
            paths={path: {method: {"summary": f"{method.upper()} {path}", "responses": {"200": {"description": "Success"}}} for method in methods} for path, methods in paths.items()},
        )

    @app.get("/swagger")
    def swagger_page():
        return (
            "<!doctype html><html><head><title>AcxiomCRM API</title></head><body>"
            "<h1>AcxiomCRM API</h1><p>OpenAPI JSON: <a href='/api/docs'>/api/docs</a></p>"
            "</body></html>"
        )

    @app.get("/")
    def frontend_index():
        return send_from_directory(FRONTEND_DIRECTORY, "index.html")

    @app.get("/<path:asset>")
    def frontend_asset(asset: str):
        if asset.lower().startswith(("views/", "wwwroot/")) or asset.lower().endswith(".cshtml"):
            return fail("Not found.", 404)
        return send_from_directory(FRONTEND_DIRECTORY, asset)

    @app.errorhandler(404)
    def not_found(error):
        app.logger.debug("Route not found: %s", error)
        if request.path.startswith("/api/"):
            return fail("API endpoint not found.", 404)
        return send_from_directory(FRONTEND_DIRECTORY, "index.html")

    @app.errorhandler(500)
    def server_error(error):
        app.logger.exception("Unexpected application error", exc_info=error)
        return fail("An unexpected server error occurred.", 500)

    def register_crm_routes() -> None:
        schemas = crm_schemas()
        for collection in COLLECTIONS:
            endpoint_name = collection
            app.add_url_rule(
                f"/api/{collection}",
                endpoint=f"{endpoint_name}_list",
                view_func=login_required(lambda collection=collection: list_records(collection)),
                methods=["GET"],
            )
            app.add_url_rule(
                f"/api/{collection}",
                endpoint=f"{endpoint_name}_create",
                view_func=login_required(lambda collection=collection: create_record(collection, schemas[collection])),
                methods=["POST"],
            )
            app.add_url_rule(
                f"/api/{collection}/<record_id>",
                endpoint=f"{endpoint_name}_get",
                view_func=login_required(lambda record_id, collection=collection: get_record(collection, record_id)),
                methods=["GET"],
            )
            app.add_url_rule(
                f"/api/{collection}/<record_id>",
                endpoint=f"{endpoint_name}_update",
                view_func=login_required(lambda record_id, collection=collection: update_record(collection, record_id, schemas[collection])),
                methods=["PUT"],
            )
            app.add_url_rule(
                f"/api/{collection}/<record_id>",
                endpoint=f"{endpoint_name}_delete",
                view_func=login_required(lambda record_id, collection=collection: delete_record(collection, record_id)),
                methods=["DELETE"],
            )

        app.add_url_rule(
            "/api/leads/<record_id>/convert", endpoint="convert_lead",
            view_func=login_required(convert_lead), methods=["POST"],
        )
        app.add_url_rule(
            "/api/reports/pipeline", endpoint="pipeline_report",
            view_func=login_required(pipeline_report), methods=["GET"],
        )
        app.add_url_rule(
            "/api/reports/conversion", endpoint="conversion_report",
            view_func=login_required(conversion_report), methods=["GET"],
        )
        app.add_url_rule(
            "/api/reports/audit", endpoint="audit_report",
            view_func=roles_required("Admin", "Manager")(audit_report), methods=["GET"],
        )

    def pagination() -> tuple[int, int]:
        try:
            page = max(1, int(request.args.get("page", "1")))
            page_size = min(100, max(1, int(request.args.get("pageSize", "25"))))
        except ValueError:
            page, page_size = 1, 25
        return page, page_size

    def crm_schemas() -> dict[str, dict[str, Any]]:
        return {
            "customers": {"required": ["customerName", "email", "phone", "companyName"], "enums": {"status": {"Active", "Inactive"}}, "phone": True},
            "leads": {"required": ["leadName", "email", "phone", "companyName", "source"], "enums": {"source": {"Website", "Referral", "Advertisement", "Event", "Other"}, "status": {"New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"}, "priority": {"Low", "Normal", "High"}}, "phone": True},
            "opportunities": {"required": ["opportunityName", "amount", "probability", "stage", "expectedCloseDate"], "enums": {"stage": {"Qualification", "Proposal", "Negotiation", "Won", "Lost"}, "status": {"Open", "Closed"}}, "number": ["amount", "probability"]},
            "followups": {"required": ["subject", "followUpType", "followUpDate"], "enums": {"followUpType": {"Call", "Meeting", "Email", "Task"}, "status": {"Planned", "Completed", "Missed", "Cancelled"}}},
            "activities": {"required": ["subject", "activityType"], "enums": {"activityType": {"Call", "Meeting", "Email", "Task"}, "status": {"Open", "Completed", "Cancelled"}}},
        }

    def validate_payload(collection: str, schema: dict[str, Any]) -> tuple[dict[str, Any] | None, str | None]:
        data = request.get_json(silent=True)
        if not isinstance(data, dict):
            return None, "A JSON object is required."
        for field in schema["required"]:
            if field not in data or data[field] in (None, ""):
                return None, f"{field} is required."
        for field, options in schema.get("enums", {}).items():
            if field in data and data[field] not in options:
                return None, f"{field} has an invalid value."
        for field in ("email",):
            if field in data and (not isinstance(data[field], str) or not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", data[field])):
                return None, "Enter a valid email address."
        for field in ("customerName", "leadName", "opportunityName", "subject"):
            if field in data and (not isinstance(data[field], str) or not data[field].strip()):
                return None, f"{field} is required."
        if schema.get("phone") and not re.fullmatch(r"[6-9]\d{9}", str(data.get("phone", ""))):
            return None, "Phone must be a valid 10 digit mobile number."
        for field in schema.get("number", []):
            try:
                data[field] = float(data[field]) if field == "amount" else int(data[field])
            except (TypeError, ValueError):
                return None, f"{field} must be numeric."
        if "probability" in data and not 0 <= data["probability"] <= 100:
            return None, "Probability must be between 0 and 100."
        if "amount" in data and not 0 < data["amount"] <= 1_000_000_000:
            return None, "Amount must be positive."
        if "expectedValue" in data:
            try:
                data["expectedValue"] = float(data["expectedValue"])
            except (TypeError, ValueError):
                return None, "Expected value must be numeric."
        if data.get("expectedValue", 0) < 0:
            return None, "Expected value cannot be negative."
        for field in ("expectedCloseDate", "followUpDate", "activityDate"):
            if field in schema["required"] or field in data:
                parsed = parse_date(data.get(field))
                if parsed is None:
                    return None, f"{field} must be a valid date."
                data[field] = parsed
        if collection == "activities" and "activityDate" not in data:
            data["activityDate"] = utc_now()
        for field in ("customerId", "leadId", "opportunityId", "assignedTo"):
            if data.get(field) and not valid_id(data[field]):
                return None, f"{field} is not a valid record ID."
        data.pop("_id", None)
        return data, None

    def resolve_assignee(requested: str | None) -> str | None:
        if g.user["role"] == "SalesExecutive":
            return g.user["_id"]
        if not requested:
            return None
        row = db().execute(
            "SELECT id FROM users WHERE id = ? AND role = 'SalesExecutive' AND isActive = 1", (requested,)
        ).fetchone()
        return row["id"] if row else None

    def validate_relationships(collection: str, data: dict[str, Any]) -> str | None:
        links = [key for key in ("customerId", "leadId", "opportunityId") if data.get(key)]
        if collection in {"followups", "activities"} and len(links) != 1:
            return "Select exactly one related customer, lead, or opportunity."
        if collection == "opportunities" and (not links or "opportunityId" in links):
            return "Link the opportunity to a customer, a lead, or both."
        scope, params = record_scope()
        for key in links:
            table = {"customerId": "customers", "leadId": "leads", "opportunityId": "opportunities"}[key]
            if db().execute(f"SELECT 1 FROM {table} WHERE id = ? AND {scope}", [data[key], *params]).fetchone() is None:
                return "A selected CRM record is unavailable."
        return None

    def list_records(collection: str):
        configuration = COLLECTIONS[collection]
        page, page_size = pagination()
        scope, params = record_scope("r.")
        clauses, query_params = [scope], list(params)
        if request.args.get("status"):
            clauses.append("r.status = ?")
            query_params.append(request.args["status"])
        if request.args.get("stage") and collection == "opportunities":
            clauses.append("r.stage = ?")
            query_params.append(request.args["stage"])
        date_field = {"followups": "followUpDate", "activities": "activityDate"}.get(collection, "createdAt")
        for key, operator in (("from", ">="), ("to", "<")):
            if not request.args.get(key):
                continue
            parsed = parse_date(request.args[key])
            if parsed is None:
                return fail("Date filters must be valid dates.")
            if key == "to":
                parsed = parsed + timedelta(days=1)
            clauses.append(f"r.{date_field} {operator} ?")
            query_params.append(date_string(parsed))
        query = request.args.get("q", "").strip()
        if query:
            clauses.append("(" + " OR ".join(f"r.{field} LIKE ? ESCAPE '\\'" for field in configuration["search"]) + ")")
            query_params.extend([f"%{escape_like(query)}%"] * len(configuration["search"]))
        where = " AND ".join(clauses)
        rows = db().execute(
            f"""SELECT r.*, u.id AS assignedId, u.fullName AS assignedFullName, u.email AS assignedEmail
            FROM {collection} r LEFT JOIN users u ON u.id = r.assignedTo
            WHERE {where} ORDER BY r.{configuration['sort']} DESC LIMIT ? OFFSET ?""",
            [*query_params, page_size, (page - 1) * page_size],
        ).fetchall()
        total = db().execute(f"SELECT COUNT(*) AS count FROM {collection} r WHERE {where}", query_params).fetchone()["count"]
        return jsonify(items=[public_record(row) for row in rows], page=page, pageSize=page_size, totalCount=total)

    def create_record(collection: str, schema: dict[str, Any]):
        data, error = validate_payload(collection, schema)
        if error:
            return fail(error)
        if collection == "leads" and data.get("status", "New") != "New":
            return fail("New leads must start with the New status.")
        relationship_error = validate_relationships(collection, data)
        if relationship_error:
            return fail(relationship_error)
        assignee = resolve_assignee(data.get("assignedTo"))
        if not assignee:
            return fail("Assign the record to an active Sales Executive.")
        if collection == "opportunities" and data.get("status", "Open") == "Open" and is_before_today(data["expectedCloseDate"]):
            return fail("Expected Close Date cannot be in the past.")
        if collection == "opportunities" and data.get("stage") in {"Won", "Lost"} and data.get("status", "Open") != "Closed":
            return fail("Won or Lost opportunities must be Closed.")
        if collection == "followups" and (data.get("status", "Planned") != "Planned" or is_before_today(data["followUpDate"])):
            return fail("New follow-ups must be Planned and dated today or later.")
        if collection == "activities" and data.get("status", "Open") != "Open":
            return fail("New activities must start as Open.")
        data = normalize_values(data)
        data["assignedTo"] = assignee
        if collection == "customers":
            data.update(customerCode=make_code("CUS"), createdBy=g.user["_id"])
        elif collection == "leads":
            data["leadCode"] = make_code("LEAD")
        record = insert_row(collection, data)
        log_audit("Create", singular_name(collection), record["id"], new=record[COLLECTIONS[collection]["field"]])
        return jsonify(item=public_record(record)), 201

    def get_record(collection: str, record_id: str):
        if not valid_id(record_id):
            return fail("Invalid record ID.")
        scope, params = record_scope("r.")
        record = db().execute(
            f"""SELECT r.*, u.id AS assignedId, u.fullName AS assignedFullName, u.email AS assignedEmail
            FROM {collection} r LEFT JOIN users u ON u.id = r.assignedTo WHERE r.id = ? AND {scope}""",
            [record_id, *params],
        ).fetchone()
        return jsonify(item=public_record(record)) if record else fail("Record not found.", 404)

    def update_record(collection: str, record_id: str, schema: dict[str, Any]):
        if not valid_id(record_id):
            return fail("Invalid record ID.")
        scope, params = record_scope()
        record = db().execute(f"SELECT * FROM {collection} WHERE id = ? AND {scope}", [record_id, *params]).fetchone()
        if not record:
            return fail("Record not found.", 404)
        if collection == "followups" and record["status"] != "Planned":
            return fail("Only planned follow-ups can be edited.", 409)
        if collection == "activities" and record["status"] != "Open":
            return fail("Only open activities can be edited.", 409)
        data, error = validate_payload(collection, schema)
        if error:
            return fail(error)
        if collection == "leads" and (
            record["status"] == "Converted"
            or data.get("status", record["status"]) != record["status"]
            and data.get("status") not in LEAD_TRANSITIONS.get(record["status"], set())
        ):
            return fail("That lead status transition is not allowed.")
        relationship_error = validate_relationships(collection, data)
        if relationship_error:
            return fail(relationship_error)
        assignee = resolve_assignee(data.get("assignedTo")) if g.user["role"] in {"Admin", "Manager"} else record["assignedTo"]
        if not assignee:
            return fail("Assign the record to an active Sales Executive.")
        if collection == "opportunities" and data.get("status", record["status"]) == "Open" and is_before_today(data["expectedCloseDate"]):
            return fail("Expected Close Date cannot be in the past.")
        if collection == "followups" and data.get("status", record["status"]) == "Planned" and is_before_today(data["followUpDate"]):
            return fail("Follow-up date cannot be earlier than today.")
        old = record[COLLECTIONS[collection]["field"]]
        old = f"{old}; {record['status'] if 'status' in record.keys() else record['stage']}"
        data = normalize_values(data)
        data["assignedTo"] = assignee
        updated = update_row(collection, record_id, data)
        new = f"{updated[COLLECTIONS[collection]['field']]}; {updated.get('status', updated.get('stage', ''))}"
        log_audit("Update", singular_name(collection), record_id, old, new)
        return jsonify(item=public_record(updated))

    def delete_record(collection: str, record_id: str):
        if not valid_id(record_id):
            return fail("Invalid record ID.")
        scope, params = record_scope()
        record = db().execute(f"SELECT * FROM {collection} WHERE id = ? AND {scope}", [record_id, *params]).fetchone()
        if not record:
            return fail("Record not found.", 404)
        label = COLLECTIONS[collection]["field"]
        if collection == "customers":
            update_row(collection, record_id, {"status": "Inactive"})
            log_audit("Deactivate", "Customer", record_id, record[label], "Inactive")
        else:
            db().execute(f"DELETE FROM {collection} WHERE id = ?", (record_id,))
            log_audit("Delete", singular_name(collection), record_id, record[label])
        return "", 204

    def convert_lead(record_id: str):
        if not valid_id(record_id):
            return fail("Invalid lead ID.")
        scope, params = record_scope()
        lead = db().execute(f"SELECT * FROM leads WHERE id = ? AND {scope}", [record_id, *params]).fetchone()
        if not lead:
            return fail("Lead not found.", 404)
        if lead["status"] != "Qualified":
            return fail("Only qualified leads can be converted.", 409)
        duplicate = db().execute(
            "SELECT 1 FROM customers WHERE email = ? COLLATE NOCASE OR phone = ?",
            (lead["email"], lead["phone"]),
        ).fetchone()
        if duplicate:
            return fail("A customer with this email or phone already exists.", 409)
        connection = db()
        try:
            connection.execute("BEGIN IMMEDIATE")
            customer = insert_row("customers", {
                "customerCode": make_code("CUS"), "customerName": lead["leadName"],
                "email": lead["email"], "phone": lead["phone"], "companyName": lead["companyName"],
                "createdBy": g.user["_id"], "assignedTo": lead["assignedTo"],
            })
            opportunity = insert_row("opportunities", {
                "opportunityName": f"{lead['companyName']} - {lead['leadName']}",
                "customerId": customer["id"], "leadId": lead["id"],
                "amount": max(lead["expectedValue"], 0.01), "probability": 10,
                "stage": "Qualification",
                "expectedCloseDate": date_string(utc_now() + timedelta(days=30)),
                "status": "Open", "source": lead["source"], "assignedTo": lead["assignedTo"],
            })
            update_row("leads", lead["id"], {"status": "Converted"})
            log_audit("Convert", "Lead", lead["id"], "Qualified", f"Customer={customer['id']}; Opportunity={opportunity['id']}")
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        return jsonify(customer=public_record(customer), opportunity=public_record(opportunity)), 201

    def pipeline_report():
        scope, params = record_scope("o.")
        rows = db().execute(
            f"""SELECT o.*, u.fullName AS assignedFullName FROM opportunities o
            LEFT JOIN users u ON u.id = o.assignedTo WHERE {scope} AND o.status = 'Open'""",
            params,
        ).fetchall()
        open_rows = [dict(row) for row in rows]
        def group(key):
            grouped: dict[str, dict[str, Any]] = {}
            for row in open_rows:
                name = key(row) or "Unassigned"
                item = grouped.setdefault(name, {"name": name, "count": 0, "amount": 0, "weightedAmount": 0})
                item["count"] += 1
                item["amount"] += row["amount"]
                item["weightedAmount"] += row["amount"] * row["probability"] / 100
            return list(grouped.values())
        return jsonify(
            totalAmount=sum(row["amount"] for row in open_rows),
            weightedAmount=sum(row["amount"] * row["probability"] / 100 for row in open_rows),
            byStage=group(lambda row: row["stage"]),
            byOwner=group(lambda row: row["assignedFullName"]),
        )

    def conversion_report():
        scope, params = record_scope("l.")
        leads = db().execute(
            f"SELECT l.*, u.fullName AS assignedFullName FROM leads l LEFT JOIN users u ON u.id = l.assignedTo WHERE {scope}",
            params,
        ).fetchall()
        rows = [dict(row) for row in leads]
        def group(key):
            grouped: dict[str, dict[str, Any]] = {}
            for row in rows:
                name = key(row) or "Unassigned"
                item = grouped.setdefault(name, {"name": name, "total": 0, "converted": 0})
                item["total"] += 1
                item["converted"] += row["status"] == "Converted"
                item["rate"] = item["converted"] / item["total"] * 100 if item["total"] else 0
            return list(grouped.values())
        converted = sum(row["status"] == "Converted" for row in rows)
        return jsonify(
            total=len(rows), converted=converted, notConverted=len(rows) - converted,
            conversionRate=converted / len(rows) * 100 if rows else 0,
            bySource=group(lambda row: row["source"]), byOwner=group(lambda row: row["assignedFullName"]),
        )

    def audit_report():
        page, page_size = pagination()
        clauses, params = [], []
        if request.args.get("action"):
            clauses.append("a.action = ?")
            params.append(request.args["action"])
        if request.args.get("entity"):
            clauses.append("a.entityName = ?")
            params.append(request.args["entity"])
        where = f"WHERE {' AND '.join(clauses)}" if clauses else ""
        items = db().execute(
            f"""SELECT a.*, u.fullName AS auditUserName, u.email AS auditUserEmail
            FROM auditLogs a LEFT JOIN users u ON u.id = a.userId {where}
            ORDER BY a.createdAt DESC LIMIT ? OFFSET ?""",
            [*params, page_size, (page - 1) * page_size],
        ).fetchall()
        count = db().execute(f"SELECT COUNT(*) AS count FROM auditLogs a {where}", params).fetchone()["count"]
        result = []
        for row in items:
            entry = dict(row)
            entry["userId"] = (
                {"_id": row["userId"], "fullName": row["auditUserName"], "email": row["auditUserEmail"]}
                if row["userId"] else None
            )
            result.append(entry)
        return jsonify(items=result, page=page, pageSize=page_size, totalCount=count)

    register_crm_routes()


def password_is_strong(value: str) -> bool:
    return bool(
        re.search(r"[a-z]", value)
        and re.search(r"[A-Z]", value)
        and re.search(r"\d", value)
        and re.search(r"[^A-Za-z0-9]", value)
    )


def singular_name(collection: str) -> str:
    return {
        "customers": "Customer",
        "leads": "Lead",
        "opportunities": "Opportunity",
        "followups": "FollowUp",
        "activities": "Activity",
    }[collection]


def main() -> None:
    if len(os.environ.get("JWT_SECRET", "")) < 32:
        raise RuntimeError("Set JWT_SECRET to a private random value of at least 32 characters in Backend/.env.")
    app = create_app()
    with app.app_context():
        connection = get_db()
        demo_users = [
            ("admin@acxiom.local", "Admin@123", "System Admin", "Admin"),
            ("manager@acxiom.local", "Manager@123", "Sales Manager", "Manager"),
            ("sales@acxiom.local", "Sales@123", "Sales Executive", "SalesExecutive"),
        ]
        for email, password, full_name, role in demo_users:
            exists = connection.execute("SELECT 1 FROM users WHERE email = ? COLLATE NOCASE", (email,)).fetchone()
            if not exists:
                insert_row(
                    "users",
                    {
                        "fullName": full_name,
                        "email": email,
                        "passwordHash": generate_password_hash(password),
                        "role": role,
                    },
                )
    app.run(host="127.0.0.1", port=int(os.environ.get("PORT", "3000")), debug=os.environ.get("FLASK_DEBUG") == "1")


if __name__ == "__main__":
    main()
