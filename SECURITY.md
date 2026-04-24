# Security Policy

> *The Ministry takes security seriously.*
> *It has filed the appropriate forms.*
> *In triplicate. With encryption.*

---

## Supported versions

| Version          |           Supported |
|------------------|---------------------|
| Latest release   | ✓                   |
| Previous release | Security fixes only |
| Older releases   | Not supported       |

During the beta period, only the current beta release
receives security fixes.

---

## Reporting a vulnerability

**Please do not report security vulnerabilities via GitHub issues.**
GitHub issues are public. A public vulnerability report gives
attackers information before a fix is available.

Instead, report security vulnerabilities by email:

**security@myndworx.com**

Include:
- A description of the vulnerability
- Steps to reproduce
- The version of MTGB affected
- Your assessment of the impact
- Any proof-of-concept code if relevant

The Ministry will acknowledge your report within 48 hours
and provide a timeline for a fix within 7 days.

---

## What we consider a security vulnerability

- Authentication bypass or credential exposure
- API key or OAuth token leakage
- Remote code execution
- Privilege escalation
- Unintended data collection or transmission
- Injection vulnerabilities in the community API
- Insecure storage of credentials

---

## What we do not consider a security vulnerability

- SmartScreen warnings on the unsigned beta installer —
  this is expected and documented
- The absence of code signing on beta releases —
  this is in progress
- Features that are documented as not yet implemented
  (OAuth2, full uninstaller cleanup)

---

## Disclosure policy

MTGB follows responsible disclosure. Once a fix is available
and released, we will:

1. Publish a security advisory on GitHub
2. Credit the reporter (unless they prefer to remain anonymous)
3. Update the changelog with a brief, non-technical description

We ask that reporters allow reasonable time for a fix before
public disclosure. We will not take legal action against
researchers who follow this policy.

---

## Credential storage

MTGB stores API keys and OAuth tokens exclusively in the
Windows Credential Manager. They are never written to disk
in plain text and never transmitted to any server other than
SimplyPrint's API endpoints.

The anonymous install ID and all other settings are stored in:

```
%APPDATA%\MTGB\appsettings.json
```

This file contains no credentials.

---

## Community API security

The community API at `community.myndworx.com` uses:

- HTTPS only — plain HTTP is rejected
- Prepared statements throughout — no raw query interpolation
- Input validation and sanitisation on all endpoints
- Rate limiting on all client-facing endpoints
- API key authentication on the publish endpoint
- No personal data storage — see [TELEMETRY.md](TELEMETRY.md)

The complete server-side source code is published in
this repository under `server/` for full auditability.

---

*MTGB — The Monitor That Goes Bing*
*Never leave a print behind.*
*The Ministry's security team is watching.*
*It always was.*
