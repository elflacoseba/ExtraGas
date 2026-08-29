# Delta for password-reset-confirm

> First-time capability. The main spec (`openspec/specs/password-reset-confirm/spec.md`) was created from this delta. The `## ADDED Requirements` block below mirrors the requirements to be merged on archive.

## ADDED Requirements

### Requirement: Consume valid token and set new password

The system SHALL consume a valid (unused, unexpired) reset token on `POST /Account/ResetPassword`, replace the user's BCrypt password hash, and mark the token used. On success a notification email SHALL be sent to the user's registered address confirming the change (no reset link in that email).

#### Scenario: Successful password reset

- GIVEN a row in `password_reset_tokens` with `token_hash = SHA-256(rawToken)` and `used_at IS NULL` and `expires_at > NOW()`
- WHEN a POST is made to `/Account/ResetPassword` with body `{ token: rawToken, password: "NewSecure123!" }`
- THEN `password_reset_tokens.used_at` is set to NOW for that row
- AND `usuarios.password_hash` for the matching user is replaced with a fresh BCrypt hash of "NewSecure123!"
- AND a notification email is sent to the user's registered email confirming the password was changed (no reset link in this email)
- AND the response redirects to `/Account/Login` with a success TempData message

#### Scenario: Reject an expired token

- GIVEN a row in `password_reset_tokens` with `expires_at < NOW()` and `used_at IS NULL`
- WHEN a POST is made to `/Account/ResetPassword` with the corresponding raw token
- THEN `used_at` remains NULL
- AND `usuarios.password_hash` is NOT modified
- AND the response shows the error "El enlace ha expirado. Solicitá uno nuevo."
- AND no notification email is sent

#### Scenario: Reject an already-used token

- GIVEN a row in `password_reset_tokens` with `used_at IS NOT NULL`
- WHEN a POST is made to `/Account/ResetPassword` with the corresponding raw token
- THEN `usuarios.password_hash` is NOT modified
- AND the response shows the error "Este enlace ya fue utilizado."
- AND no notification email is sent

#### Scenario: Reject an unknown token

- GIVEN no row in `password_reset_tokens` matches the supplied token's hash
- WHEN a POST is made to `/Account/ResetPassword` with body `{ token: "never-issued", password: "..." }`
- THEN the response shows the error "Enlace inválido."
- AND no DB write occurs

### Requirement: Render reset form on GET

The system SHALL render the reset-password form on `GET /Account/ResetPassword?token={rawToken}` without consuming the token. The token SHALL be round-tripped to the POST action via a hidden form field.

#### Scenario: GET with valid token renders the reset form

- GIVEN a row in `password_reset_tokens` exists with valid (unused, unexpired) hash matching `rawToken`
- WHEN a GET is made to `/Account/ResetPassword?token={rawToken}`
- THEN the response is HTTP 200 rendering the reset form
- AND the form posts back to `/Account/ResetPassword` including the token in a hidden field
- AND `used_at` is NOT set by the GET
