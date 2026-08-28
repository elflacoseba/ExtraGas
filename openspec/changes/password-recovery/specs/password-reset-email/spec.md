# Delta for password-reset-email

> First-time capability. The main spec (`openspec/specs/password-reset-email/spec.md`) was created from this delta. The `## ADDED Requirements` block below mirrors the requirements to be merged on archive.

## ADDED Requirements

### Requirement: Issue reset token on request

The system SHALL issue a single-use, time-limited reset token when `POST /Account/ForgotPassword` is received for a registered email. The raw token SHALL be transmitted only via the emailed link; only its SHA-256 hash SHALL be persisted in `password_reset_tokens`.

#### Scenario: Request reset for a registered email

- GIVEN a user account exists in `usuarios` with `email = foo@example.com`
- AND requester IP is `203.0.113.42` and User-Agent is `Mozilla/5.0 ...`
- WHEN a POST is made to `/Account/ForgotPassword` with body `{ email: "foo@example.com" }`
- THEN a row is inserted into `password_reset_tokens` with `usuario_id` matching that user
- AND `token_hash` is a 64-character hex string (SHA-256)
- AND `expires_at` is approximately now + 1 hour (±2 minutes)
- AND `used_at` is NULL
- AND `ip_address` is `203.0.113.42`
- AND an email is sent to `foo@example.com` containing a link `/Account/ResetPassword?token={rawToken}`
- AND the HTTP response is 200 with the generic message "Si tu dirección de correo está registrada, recibirás un enlace para restablecer tu contraseña."

#### Scenario: Request reset for an unregistered email

- GIVEN no user exists with `email = unknown@example.com`
- WHEN a POST is made to `/Account/ForgotPassword` with body `{ email: "unknown@example.com" }`
- THEN NO row is inserted into `password_reset_tokens`
- AND NO email is sent
- AND the HTTP response is 200 with the SAME generic message as the registered-email case
- AND response timing is within ±200ms of the registered-email path (no timing-based enumeration)

### Requirement: Rate limit forgot-password requests

The system SHALL cap password-reset requests at 3 POSTs per IP per rolling hour. Over-limit requests SHALL return HTTP 200 with the generic message and MUST NOT insert rows or send email.

#### Scenario: Fourth request from same IP within one hour is suppressed

- GIVEN requester IP `198.51.100.7` has submitted 3 POSTs to `/Account/ForgotPassword` within the last hour
- WHEN a 4th POST is made from the same IP to `/Account/ForgotPassword` with any email
- THEN the HTTP response is 200 with the generic message
- AND NO row is inserted into `password_reset_tokens`
- AND NO email is sent
- AND the rate-limit window resets 1 hour after the first request

### Requirement: Tokens never persisted in plaintext

The system SHALL store only the SHA-256 hash of reset tokens. The raw token SHALL be transmitted only via email link and SHALL NOT be persisted anywhere in `password_reset_tokens`.

#### Scenario: Stored token_hash values are SHA-256 hex; raw token is not persisted

- GIVEN any number of reset tokens have been issued
- WHEN querying `SELECT token_hash FROM password_reset_tokens WHERE deleted_at IS NULL`
- THEN every `token_hash` is exactly 64 lowercase hex characters
- AND no row contains the raw token that was emailed
