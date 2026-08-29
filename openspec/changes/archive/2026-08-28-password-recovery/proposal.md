# Proposal: password-recovery

## Intent

Users who forget their password currently see "contacta al administrador" on the login page — the only recovery path is an admin manually calling `UsuariosController.ResetPassword`. This change adds a self-service password-reset flow: user enters their email on a new `/Account/ForgotPassword` page, receives a time-limited link via SMTP email, clicks it, and sets a new password. Admin-assisted reset (`UsuariosController.ResetPassword`) continues to work unchanged.

## Scope

### In Scope
- New `password_reset_tokens` table (dedicated, not Identity)
- `AccountController.ForgotPassword` GET/POST (anonymous, rate-limited)
- `AccountController.ResetPassword` GET/POST (anonymous, token-bearer)
- `IUsuarioService.RequestPasswordResetAsync(email)` — creates token record, sends email via MailKit
- `IUsuarioService.ConsumePasswordResetTokenAsync(token, newPassword)` — validates token, updates BCrypt hash, marks token used
- New SMTP configuration section in `appsettings.json`
- New `ForgotPassword.cshtml` and `ResetPassword.cshtml` Razor views
- Link added to `Login.cshtml` ("¿Olvidaste tu contraseña?")
- Migration: `db/migrations/<date>_create_password_reset_tokens.sql`
- Post-reset notification email (confirms password was changed, not a reset link)

### Out of Scope
- ASP.NET Identity migration
- Two-factor authentication (2FA)
- OAuth / SSO / social login
- Account lockout beyond rate limiting on `/Account/ForgotPassword`
- Email-change verification flow
- Audit dashboard UI for password resets

## Capabilities

> Contract with sdd-spec. Research `openspec/specs/` before filling this in. Currently `openspec/specs/` contains no user/auth-related specs — this is a **new capability**.

### New Capabilities
- `password-reset-email`: Allows a user to request a time-limited, single-use password-reset token delivered to their registered email address. Token is cryptographically random (32 bytes), SHA-256 hashed before storage, valid for 1 hour, single-use, and capped at 3 requests per IP per hour. The system never reveals whether an email address is registered.
- `password-reset-confirm`: Allows a user who holds a valid, unexpired, unused reset token to set a new password. On success the token is marked `used_at`, a notification email is sent to the user's address, and the new password hash (BCrypt) replaces the existing one.

### Modified Capabilities
- None

## Approach

Option A (custom + dedicated `password_reset_tokens` table) is selected. This avoids ASP.NET Identity overhead and the stateless DataProtection token approach. It provides full control over token lifecycle, expiry, and single-use enforcement, and is consistent with the existing custom-auth architecture (cookie auth, BCrypt, no Identity).

**Token lifecycle:**
1. `IUsuarioService.RequestPasswordResetAsync(email)` — looks up user by email, generates 32-byte CSPRNG token (via `RandomNumberGenerator`), stores SHA-256 hash of the token (not the raw token) in `password_reset_tokens` with `expires_at = now + 1h`, logs IP and User-Agent. Returns void to the caller; sends email asynchronously.
2. User clicks link in email: `GET /Account/ResetPassword?token={rawToken}` — controller receives raw token, passes to view for display. No DB read on GET.
3. User submits new password: `POST /Account/ResetPassword` — calls `IUsuarioService.ConsumePasswordResetTokenAsync(rawToken, newPassword)`. Service computes SHA-256 of token, looks up by hash where `used_at IS NULL AND expires_at > now`, marks `used_at`, updates user's BCrypt hash. Sends notification email to user's address.

**Existing `TemporaryPasswordGenerator` precedent** (`Services/TemporaryPasswordGenerator.cs`): uses `RandomNumberGenerator` (CSPRNG) and Fisher-Yates shuffle. The reset-token generation follows the same pattern but produces 32 raw bytes encoded as URL-safe Base64 (43 chars), never stored in plaintext.

**Rate limiting**: in-memory `IMemoryCache` counter keyed by IP on `/Account/ForgotPassword` POST. Counter incremented per request; reset after 1 hour. Over-limit requests return `200 OK` with a generic message (no enumeration).

**No email enumeration**: both the "email sent" and "email not found" paths return the identical message: "Si tu dirección de correo está registrada, recibirás un enlace para restablecer tu contraseña." This prevents attackers from probing for registered accounts.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Controllers/AccountController.cs` | Modified | Add `ForgotPassword` GET/POST, `ResetPassword` GET/POST actions |
| `Services/Interfaces/IUsuarioService.cs` | Modified | Add `RequestPasswordResetAsync`, `ConsumePasswordResetTokenAsync` |
| `Services/Implementations/UsuarioService.cs` | Modified | Implement both methods |
| `Data/Entities/` | New | `PasswordResetToken.cs` entity |
| `Data/Configurations/` | New | `PasswordResetTokenConfiguration.cs` |
| `DTOs/` | New | `ForgotPasswordDto.cs`, `ResetPasswordDto.cs` |
| `db/migrations/<date>_create_password_reset_tokens.sql` | New | Table DDL with indexes, FK, audit columns |
| `Views/Account/ForgotPassword.cshtml` | New | Email input form |
| `Views/Account/ResetPassword.cshtml` | New | New password form (token in hidden field) |
| `Views/Account/Login.cshtml` | Modified | Add "Olvidaste tu contraseña?" link above the admin contact line |
| `Views/Shared/_AccountLayout.cshtml` | No change | Layout already suitable |
| `Program.cs` | Modified | Register MailKit email service, configure SMTP from IOptions |
| `appsettings.json` | Modified | Add `Email` section (Host, Port, UseSsl, FromAddress, FromDisplayName) |
| `appsettings.Development.json` | Modified | Add `Email` section with MailHog defaults |

## Database Change

**Migration file**: `db/migrations/<YYYYMMDD>_000001_create_password_reset_tokens.sql`

```sql
CREATE TABLE IF NOT EXISTS `password_reset_tokens` (
  `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `usuario_id` BIGINT UNSIGNED NOT NULL,
  `token_hash` VARCHAR(64) NOT NULL COMMENT 'SHA-256 of the raw token, stored hex',
  `ip_address` VARCHAR(45) NULL COMMENT 'IPv4 or IPv6 of requester',
  `user_agent` VARCHAR(500) NULL,
  `expires_at` DATETIME NOT NULL COMMENT 'UTC expiry time',
  `used_at` DATETIME NULL COMMENT 'NULL = unused; set on consume',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` BIGINT UNSIGNED NULL,
  `updated_by` BIGINT UNSIGNED NULL,
  `deleted_at` DATETIME NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_token_hash` (`token_hash`),
  KEY `idx_usuario_used` (`usuario_id`, `used_at`),
  KEY `idx_expires_at` (`expires_at`),
  CONSTRAINT `fk_password_reset_tokens_usuario`
    FOREIGN KEY (`usuario_id`) REFERENCES `usuarios` (`id`)
    ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='One-time password-reset tokens; token stored as SHA-256 hash';
```

**Indexes**:
- `uk_token_hash`: enforces single-use (one token = one record) and enables O(1) lookup by hash
- `idx_usuario_used`: supports "active tokens for user" queries and single-use enforcement
- `idx_expires_at`: supports cleanup queries for expired tokens

**Soft delete**: `deleted_at` allows logical removal without destroying audit history.

## Configuration

**`appsettings.json`** — new section:

```json
{
  "Email": {
    "Host": "smtp.example.com",
    "Port": 587,
    "UseSsl": true,
    "FromAddress": "noreply@extragas.com",
    "FromDisplayName": "ExtraGas"
  }
}
```

**`appsettings.Development.json`** — MailHog dev defaults:

```json
{
  "Email": {
    "Host": "localhost",
    "Port": 1025,
    "UseSsl": false,
    "FromAddress": "noreply@localhost",
    "FromDisplayName": "ExtraGas Dev"
  }
}
```

Production credentials (SMTP username/password) via `dotnet user-secrets`:
`dotnet user-secrets set "Email:Username" "..." --project src/ExtraGasMVC`
`dotnet user-secrets set "Email:Password" "..." --project src/ExtraGasMVC`

## Security Checklist

| Requirement | Implementation |
|-------------|----------------|
| Token randomness | `RandomNumberGenerator.GetBytes(32)` — cryptographically secure |
| Token storage | SHA-256 hash only; raw token never persisted |
| Single-use | `used_at` set atomically on consume; token rejected if non-null |
| Expiry | `expires_at` CHECK at consume time; tokens past expiry rejected |
| IP/UA logging | Stored at creation time for audit |
| No email enumeration | Identical response whether email exists or not |
| No user enumeration | Action does not reveal whether username exists |
| CSRF protection | `[ValidateAntiForgeryToken]` on all POST actions |
| Rate limiting | 3 POSTs/IP/hour on `/Account/ForgotPassword`; returns 200 over limit |
| Secure transport | HTTPS enforced via `app.UseHttpsRedirection()` (already present) |
| Timing-safe comparison | BCrypt password comparison uses `BCrypt.Verify`; token lookup by hash uses indexed equality (not string comparison timing) |
| Admin flow preserved | `UsuariosController.ResetPassword` + `TemporaryPasswordGenerator` unchanged |

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Email deliverability (spam/junk) | High | SPF/DKIM/DMARC DNS records; "noreply" sender; user instructions to check spam |
| SMTP misconfiguration causing silent failures | High | Fire-and-forget email with try/catch; logged errors; admin sees failures in app logs |
| Dev environment: SMTP credentials not set | Medium | MailHog localhost:1025 requires no auth; Development.json pre-configured |
| User Secrets not set in production | Medium | Startup validation: if `Email:Host` present but credentials absent, log warning but do not crash |
| BCrypt hash compatibility (version mismatch) | Low | `BCrypt.Net.BCrypt.HashPassword` / `Verify` — existing password hashes unaffected |
| Admin reset flow broken by accident | Low | Separate `IUsuarioService.ResetPasswordAsync` method; `UsuariosController` unchanged |
| Token collision (hash clash) | Negligible | SHA-256 output space (2²⁵⁶) makes collision functionally impossible |

## Assumptions to Confirm

> The user can veto any of these during review. Contact the user explicitly if any assumption is incorrect.

- **Token expiry**: 1 hour from issue (`expires_at = CreatedAt + 1h`). Token presented after expiry shows error message and user must request again.
- **Rate limiting**: 3 POST requests per IP per hour on `/Account/ForgotPassword`. Over-limit requests return HTTP 200 with generic "Si tu dirección..." message (no 429).
- **Information disclosure**: `/Account/ForgotPassword` POST always returns the same response regardless of whether the email is registered.
- **Post-reset notification email**: when a password is successfully changed via reset link, a separate notification email is sent to the user's address (confirms the change, not the reset link).
- **Admin reset unchanged**: `UsuariosController.ResetPassword` and `TemporaryPasswordGenerator` remain functional and are not modified.

## Success Criteria

- [ ] User can request a password reset by entering their registered email on `/Account/ForgotPassword`
- [ ] User receives a reset email within 60 seconds of submitting the form (when SMTP is configured correctly)
- [ ] Reset link expires after 1 hour (token rejected with appropriate message)
- [ ] Reset link is single-use (second attempt with same token shows "Token inválido o expirado")
- [ ] After successful reset, user receives a notification email at their address confirming the password was changed
- [ ] `/Account/ForgotPassword` always returns HTTP 200 even when IP is over rate-limit or email not registered
- [ ] New password is hashed with BCrypt (same algorithm as existing passwords)
- [ ] Admin-assisted reset via `UsuariosController.ResetPassword` continues to work without modification
- [ ] `dotnet build src/ExtraGasMVC` succeeds with zero errors
- [ ] SQL smoke: `SELECT * FROM password_reset_tokens WHERE deleted_at IS NULL` returns the newly created record

## Verification Plan

**Manual smoke flow** (in order):

1. **Request reset (registered email)**:
   - POST `/Account/ForgotPassword` with a known user's email
   - Expected: HTTP 200, generic "recibirás un enlace" message
   - Check: `SELECT * FROM password_reset_tokens WHERE deleted_at IS NULL` shows new row with `used_at = NULL` and future `expires_at`

2. **Click reset link**:
   - Open link in email (token in query string)
   - Expected: GET `/Account/ResetPassword?token=...` shows the reset form

3. **Submit new password**:
   - POST `/Account/ResetPassword` with new password
   - Expected: redirect to `/Account/Login` with success TempData message
   - Check: `SELECT used_at FROM password_reset_tokens WHERE id = <token_id>` is not null

4. **Login with new password**:
   - POST `/Account/Login` with new credentials
   - Expected: login succeeds, redirect to Home

5. **Attempt reuse of consumed token**:
   - POST `/Account/ResetPassword` with the same token
   - Expected: error "Token inválido o expirado"

6. **Request reset (unknown email)**:
   - POST `/Account/ForgotPassword` with non-existent email
   - Expected: HTTP 200 with generic message (no indication email doesn't exist)

7. **Admin reset still works**:
   - POST `/Usuarios/ResetPassword/<userId>` (admin action)
   - Expected: temp password generated, `debe_cambiar_password = true` on user record

**SQL verification**:
```sql
-- After step 1:
SELECT id, usuario_id, LEFT(token_hash, 8) AS token_prefix, expires_at, used_at, ip_address
FROM password_reset_tokens
WHERE deleted_at IS NULL
ORDER BY created_at DESC LIMIT 5;

-- After step 3:
SELECT used_at FROM password_reset_tokens WHERE id = <id>;  -- must NOT be null
```

## Rollback Plan

1. Revert the migration file: `DROP TABLE IF EXISTS password_reset_tokens;` (FK `ON DELETE CASCADE` cleans up related records)
2. Revert `IUsuarioService`, `UsuarioService` — remove the two new methods
3. Revert `AccountController` — remove `ForgotPassword` and `ResetPassword` actions
4. Revert `Login.cshtml` — remove the "Olvidaste tu contraseña?" link
5. Revert `appsettings.json` / `appsettings.Development.json` — remove `Email` section
6. Revert `Program.cs` — remove email service registration and SMTP configuration
7. No user password hashes are affected — only the new `password_reset_tokens` table is added and dropped
8. If deployed: rebuild, restart app, run `DROP TABLE IF EXISTS password_reset_tokens` against the database

## Dependencies

- `usuarios.email` column exists as `VARCHAR(150) NULL` — no schema change needed for the user lookup
- `BCrypt.Net.BCrypt` already in use — same hashing for new passwords
- `TemporaryPasswordGenerator` already uses `RandomNumberGenerator` — same CSPRNG pattern for token generation
- MailKit package (to be added via `dotnet add package MailKit`)
- `IMemoryCache` already registered via `builder.Services.AddMemoryCache()` in `Program.cs`
- `app.UseHttpsRedirection()` already present in `Program.cs`
