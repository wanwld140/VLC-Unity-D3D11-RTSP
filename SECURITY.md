# Security policy

Do not report camera URLs, credentials, tokens, or private stream samples in a
public issue. Report dependency vulnerabilities with the exact pinned version,
affected platform, and a minimal redacted reproduction.

This project redacts RTSP URLs from its own LibVLC diagnostic path, but upstream
native logs and operating-system crash dumps may still contain sensitive data.
Review artifacts before sharing them.
