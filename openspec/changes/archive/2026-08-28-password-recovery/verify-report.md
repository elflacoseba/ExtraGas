# Verify Report: password-recovery

## 1. Summary

**Verdict: READY FOR ARCHIVE** — confidence **HIGH**.

The 4-PR chained delivery (PR1 #93, PR2 #94, PR3 #95, PR4 #96) is complete and merged into `develop`. Build is clean (0 errors, 0 new warnings); all 33 tests pass. All 9 spec scenarios are wired end-to-end against the documented file:line ranges. The HIGH known limitation (lockout not cleared on password reset) is **explicitly in scope** of design §Services step 4 ("only mutate password_hash + debe_cambiar_password") and out-of-scope per the proposal — flagged for follow-up but not a blocker for archive.

**Confidence factors**: source-inspection covers 100% of changed code; runtime evidence covers build + 33 tests; remaining evidence (DB migration apply, smoke flow with MailHog) is user-runnable per T14/T15 contract and documented in PR #96 body. No regressions to admin `UsuariosController.ResetPassword` / `TemporaryPasswordGenerator`. No new CodeQL alerts in changed files (10 pre-existing open alerts are all in `Views/AuditoriaLogins/Index.cshtml`, untouched by this change).

## 2. Build & Test Results

| Check | Command | Result |
|---|---|---|
| Solution build | `dotnet build ExtraGasMVC.sln` | ✅ 0 errors, 5 warnings (all pre-existing: 4× NU1903 AutoMapper 12.0.1 GHSA-rvv3-g6hj-g44x vulnerability advisory, 1× CS8602 dereference in `Views/Recepciones/Create.cshtml:62`). **0 new warnings**. |
| Test run | `dotnet test --nologo` | ✅ **33 passed, 0 failed, 0 skipped** (Duration: 25 ms) |

## 3. Spec Scenario Traceability

| # | Spec scenario | Implementation | Status |
|---|---|---|---|
| 1 | `password-reset-email` › Request reset for a registered email | `AccountController.ForgotPassword(POST)` lines 191–219 → `_usuarioService.RequestPasswordResetAsync` (`UsuarioService.cs:365–415`) inserts `PasswordResetToken` (lines 384–395) + fires email via `SendEmailFireAndForget` (lines 80–95) using `EmailTemplates.ResetLink` (EmailTemplates.cs:24–37) | ✅ |
| 2 | `password-reset-email` › Request reset for an unregistered email | `UsuarioService.RequestPasswordResetAsync` lines 372–379: `IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.Activo && u.DeletedAt == null)` returns null → silent return, no insert, no email. Controller still returns generic TempData["Info"] message (AccountController.cs:217) | ✅ |
| 3 | `password-reset-email` › Rate limit on `/Account/ForgotPassword` | `AccountController.cs:199–213`: cache key `password-reset:forgot:{ip}`, 1h TTL, max 3. Over-limit returns generic `TempData["Info"]` and **skips the service call** (lines 206–211) — no DB write, no email | ✅ |
| 4 | `password-reset-email` › Reset token never stored in plaintext | `UsuarioService.HashToken` lines 67–72: SHA-256 hex via `SHA256.HashData` + `Convert.ToHexString().ToLowerInvariant()` (64 lowercase hex chars). Entity `PasswordResetToken.TokenHash` (PasswordResetToken.cs:11) is the only persisted form; raw is passed only to `EmailTemplates.ResetLink` (line 414) | ✅ |
| 5 | `password-reset-confirm` › Successful password reset | `UsuarioService.ConsumePasswordResetTokenAsync` lines 417–485: atomic `ExecuteUpdateAsync` (lines 433–443) sets `UsedAt`; on `rowsAffected == 1` updates `PasswordHash` with `BCrypt.HashPassword(newPassword, workFactor: 11)` (line 465), `DebeCambiarPassword = false`, fires `PasswordChanged` email (lines 472–476). Controller maps `Success` → redirect to `Login` + `TempData["Success"]` (AccountController.cs:248–250) | ✅ |
| 6 | `password-reset-confirm` › Reject an expired token | `ExecuteUpdateAsync` includes `&& rt.ExpiresAt > now` (line 437); on `rowsAffected == 0`, `DiagnoseFailedConsumeAsync` (lines 491–508) finds row with `ExpiresAt <= now` (line 504) → returns `ConsumeResetTokenOutcome.ExpiredToken`. Controller maps to `TempData["Error"] = "El enlace ha expirado. Solicitá uno nuevo."` (AccountController.cs:256–258) | ✅ |
| 7 | `password-reset-confirm` › Reject an already-used token | `ExecuteUpdateAsync` includes `&& rt.UsedAt == null` (line 436); `DiagnoseFailedConsumeAsync` checks `token.UsedAt is not null` (line 501) → `AlreadyUsed`. Controller: `"Este enlace ya fue utilizado."` (AccountController.cs:260–262) | ✅ |
| 8 | `password-reset-confirm` › Reject an unknown token | `ExecuteUpdateAsync` returns `rowsAffected == 0`; `DiagnoseFailedConsumeAsync` finds `token is null` (line 498) → `InvalidToken`. Controller: `"Enlace inválido."` (AccountController.cs:264–266). No DB write (UPDATE was a no-op) | ✅ |
| 9 | `password-reset-confirm` › Render reset form on GET | `AccountController.ResetPassword(string? token)` lines 227–230: pure passthrough — `View(new ResetPasswordDto { Token = token ?? string.Empty })`. No `_context`, no service call. View `ResetPassword.cshtml:19` includes `<input asp-for="Token" type="hidden" />`. `used_at` untouched | ✅ |

**Result: 9 / 9 scenarios PASS.**

## 4. Task Acceptance Verification

| Task | Description | Status | Evidence |
|---|---|---|---|
| T1 | Add MailKit NuGet package | ✅ Met | `ExtraGasMVC.csproj:14`: `<PackageReference Include="MailKit" Version="4.17.0" />`. Design §Dependencies allowed latest 4.8.x+ — 4.17.0 is the current 4.x compatible with .NET 10. |
| T2 | Create migration | ✅ Met | `db/migrations/20260828_000004_create_password_reset_tokens.sql` matches design §Database DDL exactly: BIGINT UNSIGNED PK, audit cols nullable, `token_hash VARCHAR(64)`, `ip_address VARCHAR(45)`, `user_agent VARCHAR(500)`, `uk_token_hash` UNIQUE, `idx_usuario_used`, `idx_expires_at`, FK `fk_password_reset_tokens_usuario` ON DELETE CASCADE, InnoDB utf8mb4. Idempotent `CREATE TABLE IF NOT EXISTS`. |
| T3 | Entity + EF config + DbSet | ✅ Met | `Data/Entities/PasswordResetToken.cs` (POCO, 11 columns + nav), `Data/Configurations/PasswordResetTokenConfiguration.cs` (snake_case + maxlengths + indexes + `HasQueryFilter(rt => rt.DeletedAt == null)` line 74), `Data/Context/ExtraGasDbContext.cs:28` adds `PasswordResetTokens` DbSet in "Personas y seguridad" group. |
| T4 | Result type + IUsuarioService | ✅ Met | `Services/ConsumeResetTokenResult.cs` defines `enum ConsumeResetTokenOutcome { Success, InvalidToken, ExpiredToken, AlreadyUsed, WeakPassword, UnknownError }` + record. `Services/Interfaces/IUsuarioService.cs:42,50` declares both methods with XML docs. |
| T5 | Email infrastructure | ✅ Met | `Configuration/EmailOptions.cs` (Host/Port/UseSsl/FromAddress/FromDisplayName/Username?/Password?/BaseUrl, `SectionName="Email"`), `Services/Interfaces/IEmailSender.cs` (SendAsync signature), `Services/Implementations/MailKitEmailSender.cs` (`using SmtpClient` line 50, `SecureSocketOptions.StartTlsWhenAvailable` line 53, try/catch log+swallow lines 35–69), `Services/Implementations/EmailTemplates.cs` (ResetLink + PasswordChanged Spanish; PasswordChanged has no link). |
| T6 | Wire EmailOptions + appsettings | ✅ Met | `Program.cs:47–48` (`Configure<EmailOptions>`), `Program.cs:72` (`AddScoped<IEmailSender, MailKitEmailSender>()`), `Program.cs:84–100` (startup warning if prod Host set without Username/Password — does NOT crash). Both appsettings have non-secret Email sections; Dev = `localhost:1025` MailHog. |
| T7 | Implement IUsuarioService methods | ✅ Met | `UsuarioService.cs:365–415` (RequestPasswordResetAsync: IgnoreQueryFilters lookup + Activo + DeletedAt==null, null/inactive → silent return; insert token; fire-and-forget email via Task.Run + fresh DI scope + swallow); `:417–485` (ConsumePasswordResetTokenAsync: policy validate → WeakPassword; atomic ExecuteUpdateAsync WHERE TokenHash==hash && UsedAt==null && ExpiresAt>UtcNow; on rowsAffected==1 update user BCrypt + DebeCambiarPassword=false + fire-and-forget PasswordChanged; on rowsAffected==0 diagnose via IgnoreQueryFilters SELECT → InvalidToken/AlreadyUsed/ExpiredToken; DbUpdateException/DbException → UnknownError). BCrypt `workFactor: 11` constant line 22. |
| T8 | DTOs | ✅ Met | `DTOs/ForgotPasswordDto.cs` (Email `[Required][EmailAddress][StringLength(150)]`), `DTOs/ResetPasswordDto.cs` (Token `[Required]`, NewPassword `[Required][DataType(Password)]`, ConfirmPassword `[Required][DataType(Password)][Compare(nameof(NewPassword))]`). |
| T9 | AccountController actions | ✅ Met | All 4 actions present: `ForgotPassword()` GET (180–184), `ForgotPassword(ForgotPasswordDto)` POST (191–219), `ResetPassword(string? token)` GET (225–230), `ResetPassword(ResetPasswordDto, ct)` POST (236–272). `[ValidateAntiForgeryToken]` on both POSTs (lines 192, 237). Rate-limit cache key `password-reset:forgot:{ip}` (line 199). Generic "Si tu dirección..." TempData on both paths (lines 209, 217). Outcomes mapped per design (lines 246–271). Existing `GetClientIp()`/`GetUserAgent()` reused (lines 285–300). |
| T10 | ForgotPassword.cshtml | ✅ Met | `_AccountLayout` (line 4), `<form asp-action="ForgotPassword">` (line 16), `@Html.AntiForgeryToken()` (line 17), validation summary (line 18), email input `asp-for="Email"` with autofocus (line 20), submit btn (lines 24–28), `@await Html.PartialAsync("_StatusMessage")` below card (line 39), Login link back (lines 31–35). |
| T11 | ResetPassword.cshtml | ✅ Met | `@model ResetPasswordDto` (line 1), `_AccountLayout` (line 4), `login-box` width 420px (line 7), hidden `<input asp-for="Token">` (line 19), password + confirm inputs (lines 22, 26), policy bullets from `ViewBag.PasswordPolicy` (lines 29–60 — mirrors `ChangePassword.cshtml` pattern), submit btn (lines 62–66), `_StatusMessage` partial (line 77). |
| T12 | Login.cshtml link | ✅ Met | Replaced "contacta al administrador" with `<a asp-action="ForgotPassword" class="text-decoration-none">¿Olvidaste tu contraseña?</a>` (Login.cshtml:41). Surrounding `<p>` blocks preserved (lines 40, 43). |
| T13 | dotnet build verification | ✅ Met | Build clean (see §2). |
| T14 | Apply migration | ✅ Evidence-ready | File on `develop`: `db/migrations/20260828_000004_create_password_reset_tokens.sql`. Apply command documented in PR #96 body. Idempotent (`CREATE TABLE IF NOT EXISTS`). User's job — not applied by verify phase. |
| T15 | Manual smoke flow | ✅ Evidence-ready | 7-step checklist + bonus rate-limit step + per-step SQL verification queries documented in PR #96 body. Requires MailHog + dev DB. User-runnable. |

**Result: 15 / 15 tasks met (T14 + T15 evidence-ready per user-contract).**

## 5. Security Checks

| Check | Result | Evidence |
|---|---|---|
| Antiforgery on POST actions | ✅ | `[ValidateAntiForgeryToken]` on lines 192 (ForgotPassword POST) and 237 (ResetPassword POST). Plus `@Html.AntiForgeryToken()` in both views (ForgotPassword.cshtml:17, ResetPassword.cshtml:18). |
| HTTPS redirection | ✅ | `Program.cs:147` `app.UseHttpsRedirection();` — pre-existing, untouched. |
| BCrypt (not plaintext) | ✅ | `UsuarioService.cs:465` `BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: ResetPasswordWorkFactor)` (constant `ResetPasswordWorkFactor = 11` line 22). Same algorithm as existing password hashing. |
| Token is CSPRNG | ✅ | `UsuarioService.cs:59` `RandomNumberGenerator.GetBytes(32)` — System.Security.Cryptography CSPRNG. Same pattern as existing `TemporaryPasswordGenerator`. |
| SHA-256 hash before persist | ✅ | `UsuarioService.cs:67–72` `HashToken`: `SHA256.HashData(Encoding.UTF8.GetBytes(rawToken), hash)` + `Convert.ToHexString(hash).ToLowerInvariant()`. 64 lowercase hex chars persisted (`TokenHash VARCHAR(64)`). |
| No email enumeration | ✅ | Controller returns identical `TempData["Info"] = "Si tu dirección de correo está registrada, recibirás un enlace..."` on both register/unregistered/over-limit paths (AccountController.cs:209, 217). Service returns silently for unknown/inactive users (UsuarioService.cs:378). |
| Rate limit (3/h per IP) | ✅ | `IMemoryCache` counter `password-reset:forgot:{ip}`, TTL 1h, max 3 (AccountController.cs:199–213). Over-limit returns generic message with no service call. |
| Lockout not cleared on reset | ⚠️ **HIGH known limitation** | `ConsumePasswordResetTokenAsync` (lines 465–468) sets only `PasswordHash`, `DebeCambiarPassword`, `UpdatedAt`. Does NOT touch `IntentosFallidos` or `BloqueadoHasta`. User who resets while locked out still waits for window. Explicitly out-of-scope per design §Services step 4 and proposal risk table. Tracked in PR #96 body "Known limitations". |
| Admin path untouched | ✅ | `git diff 0102322..develop -- src/ExtraGasMVC/Controllers/UsuariosController.cs src/ExtraGasMVC/Services/TemporaryPasswordGenerator.cs` returns empty (no changes). `UsuariosController.ResetPassword` (line 195) and `TemporaryPasswordGenerator.Generate(12)` (UsuarioService.cs:353) work as before. |
| `TemporaryPasswordGenerator` untouched | ✅ | Same diff as above — file not modified. |
| Startup warning on missing SMTP creds (non-dev) | ✅ | `Program.cs:84–100` logs `LogWarning` if `Email:Host` set but `Username`/`Password` null. Does NOT crash. |

## 6. Configuration Verification

| Item | Result | Evidence |
|---|---|---|
| `appsettings.json` has `Email` section (prod-like, no creds) | ✅ | `appsettings.json:12–19`: Host=`smtp.example.com`, Port=587, UseSsl=true, FromAddress=`noreply@extragas.com`, FromDisplayName=`ExtraGas`, BaseUrl=`https://extragas.example.com`. No Username/Password. |
| `appsettings.Development.json` has `Email` section (MailHog defaults) | ✅ | `appsettings.Development.json:8–15`: Host=`localhost`, Port=1025, UseSsl=false, FromAddress=`noreply@localhost`, FromDisplayName=`ExtraGas Dev`, BaseUrl=`https://localhost:5001`. No creds (MailHog doesn't need them). |
| `EmailOptions` has all required fields | ✅ | `Configuration/EmailOptions.cs`: Host (22), Port (27), UseSsl (33), FromAddress (38), FromDisplayName (43), Username? (49), Password? (55), BaseUrl (60). `SectionName = "Email"` (17). |
| SMTP credentials NOT in source | ✅ | `grep -rn "Email:Username\|Email:Password" src/ExtraGasMVC/appsettings*.json` → no matches. UserSecretsId configured in `ExtraGasMVC.csproj:7`. Production must use `dotnet user-secrets set "Email:Username" "..." --project src/ExtraGasMVC` per `EmailOptions.cs:11–13` comment. |

## 7. CodeQL Status

`gh api .../code-scanning/alerts?ref=refs/heads/develop&state=open` returns **10 open alerts, ALL in `src/ExtraGasMVC/Views/AuditoriaLogins/Index.cshtml`** (XSS rule `cs/web/xss`, severity: error). None of the 10 alerts are in any file touched by this change. The follow-up commit `5a1e604` ("remove email subject from log message") cleared the CodeQL finding flagged in PR3 apply-progress. **No new open alerts introduced by this change.**

## 8. Smoke Flow Readiness

| Item | Status | Notes |
|---|---|---|
| MailHog setup command | ✅ Documented | `docker run -d -p 1025:1025 -p 8025:8025 --name mailhog mailhog/mailhog`. UI: http://localhost:8025. SMTP: localhost:1025 (matches appsettings.Development.json). |
| Migration apply command | ✅ Documented | `mysql -uroot extragas < db/migrations/20260828_000004_create_password_reset_tokens.sql` (or with migrator user). Verification: `SHOW TABLES LIKE 'password_reset_tokens'` + `SHOW CREATE TABLE`. Idempotent re-run. |
| 7-step smoke flow + bonus step | ✅ Documented | PR #96 body lists steps 1–7 + bonus (rate-limit), each with expected outcome + SQL verification query. |
| Per-step SQL verification | ✅ Documented | Step 1: `SELECT id, usuario_id, LEFT(token_hash, 8)..., expires_at, used_at, ip_address FROM password_reset_tokens WHERE deleted_at IS NULL ORDER BY created_at DESC LIMIT 5;`. Step 3: `SELECT used_at FROM password_reset_tokens WHERE id = <id>;`. Plus `SHOW TABLES` / `SHOW CREATE TABLE` for migration check. |

## 9. Out-of-Scope Confirmation

| Out-of-scope item | Implemented? | Evidence |
|---|---|---|
| ASP.NET Identity migration | ❌ No | `grep -rn "Identity\|UserManager\|SignInManager" src/ExtraGasMVC/` returns no new references. Cookie auth unchanged (Program.cs:23–30). |
| 2FA / OAuth / SSO | ❌ No | No new auth-related packages in csproj. No new claims/auth schemes. |
| Email-change verification | ❌ No | No `EmailChange` entity / migration / service. `UsuarioService.cs` has no `ChangeEmailAsync`. |
| Audit dashboard UI | ❌ No | No new views in `Views/Reports/` or `Views/Admin/`. |
| Distributed rate limiting (Redis) | ❌ No | Only `IMemoryCache` injected (AccountController.cs:22, 30). No `IDistributedCache`, no Redis packages. |
| Changes to `UsuariosController.ResetPassword` | ❌ No | `git diff 0102322..develop -- src/.../UsuariosController.cs` empty. |
| Changes to `TemporaryPasswordGenerator` | ❌ No | Same diff as above. |

## 10. Known Limitations

| Sev | Limitation | Source | Mitigation |
|---|---|---|---|
| **HIGH** | Lockout not cleared on password reset (`IntentosFallidos` / `BloqueadoHasta` untouched by `ConsumePasswordResetTokenAsync`). User who resets while locked out waits out the window. | `UsuarioService.cs:465–468` only sets `PasswordHash`, `DebeCambiarPassword`, `UpdatedAt`. Explicitly out-of-scope per design §Services step 4 ("only mutate password_hash + debe_cambiar_password"). Documented in PR #96 body "Known limitations". | Add 2 lines to `ConsumePasswordResetTokenAsync`: `usuario.IntentosFallidos = 0; usuario.BloqueadoHasta = null;` — follow-up issue / follow-up PR. For now: user self-services once window expires, or admin clears manually. |
| **MEDIUM** | Rate-limit counter resets on app restart (`IMemoryCache` is per-process). | `AccountController.cs:199–213`. Documented ambiguity #2 in design.md. | Acceptable for single-instance homelab. If multi-instance: swap to `IDistributedCache` without changing controller contract. |
| **LOW** | SMTP failures swallowed (fire-and-forget, log-only) per design ambiguity #3. | `MailKitEmailSender.cs:35–69` try/catch + `LogError` + swallow. `UsuarioService.SendEmailFireAndForget` (lines 80–95) catches + logs. | Admins monitor app logs. Generic user response preserved (no enumeration). |
| **LOW** | No explicit `IOptions<EmailOptions>` snapshot — uses `IOptions` (singleton, read once). | `UsuarioService.cs:41` takes `IOptions<EmailOptions>`; same pattern as `AuthLockoutOptions` line 38. | Hot-reload of Email config requires restart (documented in `EmailOptions.cs:7–9`). |

## 11. Recommendations (Follow-up Work)

1. **[HIGH-priority] Clear lockout on password reset.** Add `usuario.IntentosFallidos = 0; usuario.BloqueadoHasta = null;` inside `ConsumePasswordResetTokenAsync` (UsuarioService.cs:467 area). One-line fix, addresses the HIGH limitation. Out-of-scope for this change but should be tracked.
2. **[MEDIUM-priority] CI smoke test for the password-reset flow.** Add an integration test using `WebApplicationFactory<Program>` + Testcontainers MySQL + a fake `IEmailSender` that captures messages. Would automate T15 instead of relying on manual MailHog. Out-of-scope but high value.
3. **[LOW-priority] Switch rate-limit to distributed cache if scaling out.** Document trigger condition (multi-instance deploy).
4. **[LOW-priority] `appsettings.json` Host default (`smtp.example.com`) should arguably be empty** to force operators to configure it. Current default is intentional placeholder per proposal; not breaking.
5. **[LOW-priority] `IOptions<EmailOptions>` → `IOptionsSnapshot<EmailOptions>` if hot-reload of SMTP config is desired.** Documented in `EmailOptions.cs:7–9`.

## 12. Verdict

**READY FOR ARCHIVE.**

All acceptance criteria for the 9 spec scenarios are met in source and traceable to file:line. All 12 implementation tasks (T1–T12) are complete on `develop`. Build clean (0 errors, 0 new warnings). All 33 tests pass. The migration file is idempotent and the smoke flow is fully documented. The HIGH known limitation (lockout not cleared) is **explicitly out-of-scope** per design/proposal — flagged as a tracked follow-up but does not block archive. Admin path preserved untouched.

**Archive action**: merge delta specs from `openspec/changes/password-recovery/specs/password-reset-{email,confirm}/spec.md` into base specs at `openspec/specs/password-reset-{email,confirm}/spec.md` (they already mirror — `## ADDED Requirements` blocks are present in delta files; base specs already have the same content pre-populated from PR1 archival work). Then archive the change.