# MTGB Community API — Server

> *The Ministry maintains its own infrastructure.*
> *The scribes are tireless. The server is Ubuntu.*
> *No llamas were deployed in the making of this endpoint.*

The MTGB Community API powers three things:

- **Anonymous telemetry** — daily usage pings from opted-in installations
- **Community map** — anonymous dot-on-a-map registrations by state/territory
- **Release endpoint** — version checking and update delivery for MTGB clients

This directory contains the complete server-side source code.
It is published here in the interest of full transparency —
every claim made in [TELEMETRY.md](../TELEMETRY.md) is backed
by what you can read in this directory.

---

## Stack

| Component  | Version              |
|------------|----------------------|
| OS         | Ubuntu 22.04 LTS     |
| Web server | Apache 2.4           |
| PHP        | 8.1+                 |
| Database   | MariaDB / MySQL 8.0+ |
| TLS        | Let's Encrypt        |

---

## Directory structure

```
server/
└── mtgb/
    └── v1/
        ├── config.php             ← bootstrap, shared helpers, DB connection
        ├── config.local           ← credentials (NOT committed — see below)
        ├── config.local.example   ← template for config.local
        ├── response.php           ← shared JSON response helpers
        ├── ingest.php             ← POST /mtgb/v1/telemetry
        ├── latest.php             ← GET  /mtgb/v1/release/latest
        ├── publish.php            ← POST /mtgb/v1/release/publish
        ├── register.php           ← POST/DELETE /mtgb/v1/map/register
        ├── status.php             ← GET  /mtgb/v1/map/status/{installId}
        └── schema.sql             ← complete database schema
```

---

## API endpoints

| Method          | Endpoint                              | Auth             | Called by               |
|-----------------|---------------------------------------|------------------|-------------------------|
| `POST`          | `/mtgb/v1/telemetry`                  | User-Agent       | MTGB client             |
| `GET`           | `/mtgb/v1/release/latest`             | User-Agent       | MTGB client             |
| `POST`          | `/mtgb/v1/release/publish`            | X-Publish-Key    | GitHub Actions pipeline |
| `POST`          | `/mtgb/v1/map/register`               | User-Agent       | MTGB client             |
| `DELETE`        | `/mtgb/v1/map/register`               | User-Agent       | MTGB client             |
| `GET`           | `/mtgb/v1/map/status/{installId}`     | User-Agent       | MTGB client             |

All client-facing endpoints require a `User-Agent` header
beginning with `MTGB/`. Requests without it are rejected with 403.

---

## Deploying your own instance

If you want to self-host the community endpoint — for a fork,
a private installation, or to verify the code runs as documented
— here is the full setup process.

### 1. Requirements

- Ubuntu 22.04 or later (other distros will work with adjustments)
- Apache 2.4 with `mod_rewrite` enabled
- PHP 8.1 or later with `pdo`, `pdo_mysql`, and `json` extensions
- MariaDB 10.6+ or MySQL 8.0+

### 2. Clone the repo

```bash
git clone https://github.com/Rayvenhaus/mtgb.git
```

### 3. Create the database

```bash
mysql -u root -p < server/mtgb/v1/schema.sql
```

This creates the `mtgb_community` database, all tables,
indexes, foreign keys, and reporting views.

Create a dedicated database user — do not use root:

```sql
CREATE USER 'mtgb'@'localhost' IDENTIFIED BY 'your-password';
GRANT SELECT, INSERT, UPDATE, DELETE ON mtgb_community.* TO 'mtgb'@'localhost';
FLUSH PRIVILEGES;
```

### 4. Configure the API

Copy the example config and fill in your credentials:

```bash
cp server/mtgb/v1/config.local.example server/mtgb/v1/config.local
chmod 640 server/mtgb/v1/config.local
nano server/mtgb/v1/config.local
```

`config.local` is an INI file, not PHP — Apache cannot serve it
directly even if misconfigured. The API will refuse to start
if `config.local` is missing or malformed. It will fail loudly.
The Ministry insists on this.

### 5. Deploy to web root

Copy the API files to your web root:

```bash
cp -r server/mtgb /var/www/your-site/mtgb
```

Or symlink if you prefer to keep the repo as the source of truth:

```bash
ln -s /path/to/mtgb/server/mtgb /var/www/your-site/mtgb
```

### 6. Configure Apache

Enable `mod_rewrite` if not already enabled:

```bash
a2enmod rewrite
```

Add this to your Apache vhost or `.htaccess` to route
the install ID from the URL into a GET parameter:

```apache
RewriteEngine On

# Route /mtgb/v1/map/status/{installId} to status.php
RewriteRule ^mtgb/v1/map/status/([a-f0-9\-]{36})$ /mtgb/v1/status.php?install_id=$1 [QSA,L]
```

Ensure `AllowOverride All` is set for your document root,
or add the rewrite rule directly to your vhost configuration.

### 7. TLS

The API must be served over HTTPS. MTGB clients reject
plain HTTP connections. Use Let's Encrypt:

```bash
apt install certbot python3-certbot-apache
certbot --apache -d your-domain.com
```

### 8. Add .gitignore entry

If you are working from a clone, ensure `config.local`
is never committed:

```
server/mtgb/v1/config.local
```

This entry is already present in the MTGB repo `.gitignore`.

---

## Security notes

- `config.local` is INI format, not PHP — the web server
  cannot execute or serve it even if a misconfigured rewrite
  exposes it. This is intentional.
- The publish endpoint (`publish.php`) is protected by an
  API key passed in the `X-Publish-Key` header. This key
  is stored as a GitHub Actions secret and never committed.
- All inputs are validated and sanitised before touching
  the database. Prepared statements throughout. No raw
  query interpolation.
- DB errors are logged server-side and never exposed
  to the client. The client receives only a generic 503.
- Rate limiting is applied to telemetry pings
  (23 hour minimum interval) and map registrations
  (5 attempts per install ID per day).

---

## Reporting views

The schema includes five read-only views for monitoring
and community statistics:

| View                      | Description                                                    |
|---------------------------|----------------------------------------------------------------|
| `v_active_installations`  | Distinct installations that have pinged in the last 30 days    |
| `v_version_distribution`  | Install count per MTGB version                                 |
| `v_event_popularity`      | Which notification event types are most commonly enabled       |
| `v_integration_popularity`| Printer integration type breakdown                             |
| `v_map_by_country`        | Community map dot counts by country and state                  |

Query them directly from MariaDB for a snapshot of
how MTGB is being used across the community.

---

## Privacy

The server collects no personal data. No names, no IP
addresses, no credentials, no printer names, no job content.

Full details in [TELEMETRY.md](../TELEMETRY.md).

---

*MTGB — The Monitor That Goes Bing*
*Never leave a print behind.*
*The scribes are tireless, watchful, and completely anonymous.*
*No llamas were deployed in the making of this server.*
