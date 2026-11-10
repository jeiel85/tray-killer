# Security Policy

## Supported versions

Only the latest release receives fixes. Installed copies update themselves from GitHub Releases.

| Version | Supported |
|---|---|
| 1.1.x | ✅ |
| 1.0.x | ❌ — please update (1.1.0 moves the install out of the user profile) |

## Reporting a vulnerability

Please **do not open a public issue** for security problems.
Use GitHub's private reporting instead: **Security → Report a vulnerability** on this repository
(<https://github.com/jeiel85/tray-killer/security/advisories/new>).

Include the version, the steps to reproduce and the impact you expect. You will get an answer as soon as possible.

## Threat model in short

Tray Killer runs with administrator rights (through an elevated Task Scheduler logon task), so the main concern is
**privilege escalation**: a program running *without* admin rights must not be able to make Tray Killer do something on its behalf.

- The elevated exe lives in `%ProgramFiles%\TrayKiller`, which only administrators can write.
- Settings, statistics and restore data live in `%ProgramData%\TrayKiller\<user SID>`. Administrators and SYSTEM have full control, the user can only read, and inheritance is turned off. A folder there that already exists and isn't owned by Administrators or SYSTEM, or is a link, gets moved aside.
- Restore data is re-validated before every privileged write.
- The command channel (show, pause, sweep, …) only accepts the current user and never takes file paths.
- Updates come only from this repository's release downloads and must match the published SHA-256 and the expected file version.

Out of scope: anything that already requires administrator rights, and the apps you choose to target, which Tray Killer closes and blocks by design.
