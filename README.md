# AcxiomCRM

AcxiomCRM is a CRM web application with a standalone frontend, a Python/Flask API, and a local SQLite database.

## Requirements

- Python 3.10 or newer
- No separate database server is required

## Run locally

Open Command Prompt in the project folder and configure the Python/Flask backend:

```bat
cd Backend
python -m pip install -r requirements.txt
copy .env.example .env
notepad .env
```

In `.env`, replace `JWT_SECRET` with a private random value of at least 32 characters. Generate one by running this command in a separate Command Prompt window:

```bat
python -c "import secrets; print(secrets.token_hex(48))"
```

Paste the generated value after `JWT_SECRET=` in `.env` and save. You do not need to install or start a separate database server. Python's built-in SQLite support creates the local database file automatically.

Start the application from the `Backend` folder:

```bat
python app.py
```

Open <http://localhost:3000>. Flask serves the frontend and API together. API documentation is at <http://localhost:3000/swagger>; the health check is <http://localhost:3000/api/health>. Keep the terminal window open while using the application. Press **Ctrl+C** to stop it.

`python app.py` starts Flask's local development server. Do not expose it directly to the public internet.

## Demo accounts

The first server start creates these accounts if they do not already exist:

| Role | Email | Password |
| --- | --- | --- |
| Admin | `admin@acxiom.local` | `Admin@123` |
| Manager | `manager@acxiom.local` | `Manager@123` |
| Sales Executive | `sales@acxiom.local` | `Sales@123` |

Change demo passwords before deployment. Public registration creates Sales Executive accounts only. Administrators can manage user roles and issue one-hour password reset links; share those links only through a trusted channel.

## Project structure and data

- `Frontend` contains the standalone HTML, JavaScript, and CSS client.
- `Backend/app.py` is the active Flask API server and SQLite data access layer.
- `Backend/requirements.txt` lists the Python dependencies.
- `Database/acxiomcrm-python.sqlite` is created automatically and stores data for the Flask application.
- `Database/acxiomcrm.db` is the prior ASP.NET/SQLite database. It is kept as-is and is not automatically imported into the Flask database.
- The previous ASP.NET implementation is retained as legacy source and is not used by the Flask application.

Keep the database files backed up. ASP.NET Identity password hashes and existing CRM records are not automatically imported into the Flask database.

## Features

- Cookie-based sign-in, registration, password resets, account lockout, and role administration
- Role-scoped customers, leads, opportunities, follow-ups, and activities
- Qualified lead conversion, dashboard KPIs/charts, and pipeline and conversion reports
- Search, filters, pagination, CSV exports, and audit history
- Input validation, password hashing, rate limits, security headers, and API documentation
