# Tasks: password-recovery

## 1. Goal

Self-service password recovery: `/Account/ForgotPassword` issues a single-use, 1h-valid reset link via SMTP email; `/Account/ResetPassword` consumes it and sets a new BCrypt-hashed password. Admin reset untouched. Verified by `dotnet build src/ExtraGasMVC` + 7-step smoke with MailHog + SQL.

## 2. Out-of-scope reminder

No Identity, 2FA, OAuth/SSO, distributed rate limit, email-change verification, audit dashboard, lockout beyond per-IP rate-limit, or changes to `UsuariosController.ResetPassword` / `TemporaryPasswordGenerator`.

## 3. Work units

### Task 1: Add MailKit NuGet package

**Files**: `src/ExtraGasMVC/ExtraGasMVC.csproj` (mod). **Acceptance**: `<PackageReference Include="MailKit" Version="4.8.0" />` pinned; restore clean. **Lines**: 3. **Depends on**: none.

### Task 2: Create migration

**Files**: `db/migrations/20260828_000004_create_password_reset_tokens.sql` (new). **Acceptance**: DDL per design §Database — `id BIGINT UNSIGNED AUTO_INCREMENT` PK, all audit cols (`created_by`/`updated_by`/`deleted_at` NULL), `token_hash VARCHAR(64)`, `ip_address VARCHAR(45)`, `user_agent VARCHAR(500)`, `expires_at`, `used_at`; idx `uk_token_hash` unique + `idx_usuario_used` + `idx_expires_at`; FK `fk_password_reset_tokens_usuario` ON DELETE CASCADE; InnoDB utf8mb4; table comment. **Lines**: 25. **Depends on**: none.

### Task 3: Entity + EF config + DbSet

**Files**: `Data/Entities/PasswordResetToken.cs` (new); `Data/Configurations/PasswordResetTokenConfiguration.cs` (new); `Data/Context/ExtraGasDbContext.cs` (mod). **Acceptance**: POCO mirrors DDL + nav `Usuario`; config snake_case + maxlengths + indexes + `HasQueryFilter(rt => rt.DeletedAt == null)`; `DbSet<PasswordResetToken> PasswordResetTokens` in `Personas y seguridad` group. **Lines**: 86. **Depends on**: T2.

### Task 4: Result type + IUsuarioService

**Files**: `Services/ConsumeResetTokenResult.cs` (new); `Services/Interfaces/IUsuarioService.cs` (mod). **Acceptance**: `enum ConsumeResetTokenOutcome { Success, InvalidToken, ExpiredToken, AlreadyUsed, WeakPassword, UnknownError }` + record; add `RequestPasswordResetAsync(email, ipAddress, userAgent, ct)` and `ConsumePasswordResetTokenAsync(rawToken, newPassword, ct)` with XML docs. **Lines**: 22. **Depends on**: T3.

### Task 5: Email infrastructure

**Files**: `Configuration/EmailOptions.cs` (new, matches `AuthLockoutOptions` location); `Services/Interfaces/IEmailSender.cs` (new); `Services/Implementations/MailKitEmailSender.cs` (new); `Services/Implementations/EmailTemplates.cs` (new). **Acceptance**: `EmailOptions` (Host/Port/UseSsl/From*/Username?/Password?/BaseUrl, `SectionName="Email"`); `IEmailSender.SendAsync`; `MailKitEmailSender` uses `using SmtpClient` + `StartTlsWhenAvailable` + try/catch log+swallow (no body in logs); `EmailTemplates.ResetLink` + `PasswordChanged` Spanish templates, `PasswordChanged` has NO reset link. **Lines**: 187. **Depends on**: T1.

### Task 6: Wire EmailOptions + appsettings

**Files**: `Program.cs` (mod); `appsettings.json` (mod); `appsettings.Development.json` (mod). **Acceptance**: `Configure<EmailOptions>` near `Configure<PasswordPolicyOptions>` (~line 41); `AddScoped<IEmailSender, MailKitEmailSender>()`; post-build `LogWarning` if Host set but Username/Password null (no crash); non-secret Email sections in both appsettings (Dev = MailHog localhost:1025). **Lines**: 24. **Depends on**: T5.

### Task 7: Implement IUsuarioService methods

**Files**: `Services/Implementations/UsuarioService.cs` (mod). **Acceptance**: Inject `IEmailSender`/`IServiceScopeFactory`/`ILogger<UsuarioService>`. Private `GenerateRawToken` (base64url CSPRNG 32B = 43 chars) + `HashToken` (SHA-256 hex). `RequestPasswordResetAsync`: IgnoreQueryFilters lookup by email + Activo, null/inactive → silent return; insert token (expires +1h, audit null); fire-and-forget email via `Task.Run` + fresh DI scope + swallow (design ambiguity #3). `ConsumePasswordResetTokenAsync`: policy validate first → `WeakPassword`; atomic `ExecuteUpdateAsync` (Pomelo 9) WHERE `TokenHash==hash && UsedAt==null && ExpiresAt>UtcNow`; on rowsAffected==1 update user BCrypt hash + `DebeCambiarPassword=false` + fire-and-forget `PasswordChanged` email; on rowsAffected==0 diagnose (IgnoreQueryFilters SELECT) → `InvalidToken`/`AlreadyUsed`/`ExpiredToken`; DB exception → `UnknownError`. **Lines**: 130. **Depends on**: T4, T6.

### Task 8: DTOs

**Files**: `DTOs/ForgotPasswordDto.cs` (new); `DTOs/ResetPasswordDto.cs` (new). **Acceptance**: Email `[Required][EmailAddress][StringLength(150)]`; Reset has `Token [Required]`, `NewPassword [Required][DataType(Password)]`, `ConfirmPassword [Compare(nameof(NewPassword))]`. **Lines**: 44. **Depends on**: T7.

### Task 9: AccountController actions

**Files**: `Controllers/AccountController.cs` (mod). **Acceptance**: Inject `IMemoryCache`/`IEmailSender`/`IPasswordPolicyService`/`IServiceScopeFactory`; reuse existing private `GetClientIp()`/`GetUserAgent()` (lines 183–198). `[HttpGet][AllowAnonymous] ForgotPassword()` renders view. `[HttpPost][ValidateAntiForgeryToken][AllowAnonymous] ForgotPassword(ForgotPasswordDto)`: rate-limit cache key `password-reset:forgot:{ip}` TTL 1h max 3 (over → generic TempData, no service call); else call service; always show generic "Si tu dirección..." (no enumeration). `[HttpGet][AllowAnonymous] ResetPassword(string? token)` renders form, **no DB read**. `[HttpPost][ValidateAntiForgeryToken][AllowAnonymous] ResetPassword(ResetPasswordDto, ct)` maps outcomes per design §Controllers: Success → redirect Login + success TempData; ExpiredToken/AlreadyUsed/InvalidToken → TempData Error + return view; WeakPassword → ModelState errors + return view; UnknownError → generic TempData Error. **Lines**: 120. **Depends on**: T8.

### Task 10: ForgotPassword.cshtml

**Files**: `Views/Account/ForgotPassword.cshtml` (new). **Acceptance**: Layout `_AccountLayout`, AdminLTE `login-box` matching Login, `<form asp-action="ForgotPassword">` + antiforgery + validation summary, email input (`asp-for="Email"`, `type="email"`, `required`, `autofocus`), submit btn, `@await Html.PartialAsync("_StatusMessage")` below card (layout doesn't auto-include), link back to Login. **Lines**: 45. **Depends on**: T9.

### Task 11: ResetPassword.cshtml

**Files**: `Views/Account/ResetPassword.cshtml` (new). **Acceptance**: `@model ResetPasswordDto`; layout `_AccountLayout`; `login-box` width 420px; hidden `<input asp-for="Token">`; password + confirm inputs; policy bullets copied from `ChangePassword.cshtml` lines 28–59 (from `ViewBag.PasswordPolicy`); submit btn; `@await Html.PartialAsync("_StatusMessage")` below card. **Lines**: 85. **Depends on**: T10.

### Task 12: Login.cshtml link

**Files**: `Views/Account/Login.cshtml` (mod). **Acceptance**: Replace line 41 `<span>...contacta a un administrador...</span>` with `<a asp-action="ForgotPassword" class="text-decoration-none">¿Olvidaste tu contraseña?</a>`; preserve surrounding `<p>` blocks. **Lines**: 2. **Depends on**: T11.

### Task 13: dotnet build verification

**Files**: none. **Acceptance**: `dotnet build src/ExtraGasMVC` exits 0, zero new warnings. **Lines**: 0. **Depends on**: T12.

### Task 14: Apply migration

**Files**: `db/migrations/20260828_000004_create_password_reset_tokens.sql` (applied, no edit). **Acceptance**: `mysql -uroot extragas < db/migrations/20260828_000004_create_password_reset_tokens.sql` exits 0; `SHOW TABLES LIKE 'password_reset_tokens'` returns 1 row; `SHOW CREATE TABLE` confirms columns/indexes/FK; re-run is no-op. **Lines**: 0. **Depends on**: T13.

### Task 15: Manual smoke flow

**Files**: none. **Acceptance**: (1) ForgotPassword known email → row + MailHog email; (2) GET ResetPassword renders form, used_at NULL; (3) POST ResetPassword → used_at set, redirect Login + notification email; (4) Login with new password succeeds; (5) Reuse consumed token → "Este enlace ya fue utilizado."; (6) Unknown email → identical generic, no row; (7) Admin `Usuarios/ResetPassword` still works (`debe_cambiar_password=true`, temp password). Bonus: 4th ForgotPassword same IP within 1h → generic, no row. **Lines**: 0. **Depends on**: T14.

## 4. Spec ↔ task traceability

| Spec scenario | Tasks |
|---|---|
| `password-reset-email` › Request reset for registered email | T2, T3, T7, T9, T15 |
| `password-reset-email` › Request reset for unregistered email | T7, T9, T15 |
| `password-reset-email` › Rate limit on `/Account/ForgotPassword` | T9, T15 |
| `password-reset-email` › Reset token never stored in plaintext | T2, T3, T7 |
| `password-reset-confirm` › Successful password reset | T4, T7, T9, T11, T15 |
| `password-reset-confirm` › Reject expired token | T4, T7, T9, T15 |
| `password-reset-confirm` › Reject already-used token | T4, T7, T9, T15 |
| `password-reset-confirm` › Reject unknown token | T4, T7, T9, T15 |
| `password-reset-confirm` › GET renders form | T9, T11, T15 |

## 5. Review Workload Forecast

- **Total estimated changed lines**: 773 (T1 3 + T2 25 + T3 86 + T4 22 + T5 187 + T6 24 + T7 130 + T8 44 + T9 120 + T10 45 + T11 85 + T12 2)
- **400-line budget risk**: High (≈1.93× budget)
- **Chained PRs recommended**: Yes
- **Decision needed before apply**: Yes (`ask-on-risk`)
- **Rationale**: spans NuGet + migration + entity/config + DTOs + options + email infra + controller + 2 views + login touch — far too much for one PR.

### Suggested work units (chained PRs)

| Unit | Goal | PR | Base | Lines |
|------|------|----|------|-------|
| 1 | NuGet + migration + entity/config/DbSet | PR 1 | `main` | ~114 (T1+T2+T3) |
| 2 | Email infra: options + sender + templates + DI + appsettings | PR 2 | `main` (stacked) or feature branch | ~211 (T5+T6) |
| 3 | Service layer: result + interface + impl + DTOs | PR 3 | PR 1 + PR 2 | ~196 (T4+T7+T8) |
| 4 | Controller + 2 views + login link | PR 4 | PR 3 | ~252 (T9+T10+T11+T12) |
| 5 | DB apply + smoke | PR 4 commits | n/a | 0 (T13+T14+T15) |

**Chain strategy**: `stacked-to-main` natural (each PR independently shippable). `feature-branch-chain` safer for rollback. `size:exception` NOT recommended (crosses 4 layers). If user accepts `size:exception`, units 1–4 collapse to one PR (~773 lines) — SDD guard rejects without explicit exception.

## 6. Delivery strategy

`ask-on-risk` (per SDD Session Preflight). Forecast High → orchestrator MUST ask the user to choose `stacked-to-main` / `feature-branch-chain` / `size:exception` **before** `sdd-apply`. Do not proceed silently.

## 7. Order of execution

T1+T2 parallel → T3 → (T5 || T4) → T6 → T7 → T8 → T9 → T10 → T11 → T12 → T13 → T14 → T15. PR cuts: PR1=T1+T2+T3; PR2=T5+T6; PR3=T4+T7+T8; PR4=T9+T10+T11+T12.

## 8. Rollback plan

Per proposal: drop `password_reset_tokens` table; revert `IUsuarioService` + `UsuarioService` (remove 2 methods); revert `AccountController` actions; delete `ForgotPassword.cshtml` + `ResetPassword.cshtml`; revert `Login.cshtml` link; revert `Program.cs` Email wiring; revert `appsettings*.json` Email sections; uninstall MailKit. No password hashes mutated → fully reversible on data.