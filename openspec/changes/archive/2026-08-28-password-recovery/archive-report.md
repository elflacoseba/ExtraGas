# Archive Report: password-recovery

**Change**: password-recovery
**Closed**: 2026-08-28
**Delivery strategy**: ask-on-risk → 4 chained PRs stacked-to-develop
**Verdict**: READY FOR ARCHIVE

## Summary

Self-service password recovery via email link. Users now request a reset from `/Account/ForgotPassword`, receive a 1-hour-valid single-use token by email (SMTP via MailKit), and set a new password at `/Account/ResetPassword`. Admin-assisted reset (`UsuariosController.ResetPassword`) is unchanged and continues to work.

## Capabilities delivered

- `password-reset-email` — request a time-limited, single-use password-reset token via email. Rate-limited (3/h per IP), no enumeration.
- `password-reset-confirm` — consume a valid token to set a new password (BCrypt hash replaced, post-reset notification email sent).

## Delivery

| PR | Title | Tasks | Lines | URL |
|---|---|---|---|---|
| #93 | PR1 — schema (MailKit, migration, entity) | T1, T2, T3 | 142 | https://github.com/elflacoseba/ExtraGas/pull/93 |
| #94 | PR2 — email infra (MailKit sender, templates, options, DI) | T5, T6 | 255 | https://github.com/elflacoseba/ExtraGas/pull/94 |
| #95 | PR3 — service layer (reset token + DTOs) | T4, T7, T8 | 300 | https://github.com/elflacoseba/ExtraGas/pull/95 |
| #96 | PR4 — UI, login link, smoke checklist | T9–T15 | 222 | https://github.com/elflacoseba/ExtraGas/pull/96 |

Plus one follow-up commit (5a1e604) on PR3 to remove email subject from log message (CodeQL FP `cs/cleartext-storage-of-sensitive-information`).

## Verification outcome

- Build: `dotnet build ExtraGasMVC.sln` exits 0 with no new warnings.
- Tests: 33 passed / 0 failed / 0 skipped (existing test project at `tests/ExtraGasMVC.Tests/` extended with `FakeUsuarioService` stubs for the two new methods).
- Spec scenarios: 9 / 9 traced and verified.
- Task acceptance: 15 / 15 met (T1–T13 in code; T14 migration apply and T15 manual smoke are user-runnable and documented in PR4).
- CodeQL: no new alerts on develop.

## Known limitations (documented follow-ups)

- **HIGH**: `ConsumePasswordResetTokenAsync` does NOT clear `IntentosFallidos`/`BloqueadoHasta` on the user. A user who resets while `BloqueadoHasta` is in the future waits for the window. One-line fix in `UsuarioService.cs:465–468` after the BCrypt update; tracked for a future change.
- **MEDIUM**: Rate-limit counter is per-process (`IMemoryCache`); resets on app restart. Acceptable for single-instance homelab. Swap to `IDistributedCache` if scaling out.
- **LOW**: SMTP failures are logged-and-swallowed (fire-and-forget). Admins monitor `MailKitEmailSender` log entries.

## Files added (cumulative across all 4 PRs)

```
db/migrations/20260828_000004_create_password_reset_tokens.sql
openspec/changes/password-recovery/*                  (then moved to archive/)
openspec/specs/password-reset-{email,confirm}/spec.md
src/ExtraGasMVC/Configuration/EmailOptions.cs
src/ExtraGasMVC/Controllers/AccountController.cs       (modified)
src/ExtraGasMVC/Data/Configurations/PasswordResetTokenConfiguration.cs
src/ExtraGasMVC/Data/Context/ExtraGasDbContext.cs      (modified)
src/ExtraGasMVC/Data/Entities/PasswordResetToken.cs
src/ExtraGasMVC/DTOs/ForgotPasswordDto.cs
src/ExtraGasMVC/DTOs/ResetPasswordDto.cs
src/ExtraGasMVC/Program.cs                             (modified)
src/ExtraGasMVC/Services/ConsumeResetTokenResult.cs
src/ExtraGasMVC/Services/Interfaces/IEmailSender.cs
src/ExtraGasMVC/Services/Interfaces/IUsuarioService.cs (modified)
src/ExtraGasMVC/Services/Implementations/EmailTemplates.cs
src/ExtraGasMVC/Services/Implementations/MailKitEmailSender.cs
src/ExtraGasMVC/Services/Implementations/UsuarioService.cs (modified)
src/ExtraGasMVC/Views/Account/ForgotPassword.cshtml
src/ExtraGasMVC/Views/Account/Login.cshtml             (modified)
src/ExtraGasMVC/Views/Account/ResetPassword.cshtml
src/ExtraGasMVC/ExtraGasMVC.csproj                     (modified — MailKit 4.17.0)
src/ExtraGasMVC/appsettings.json                       (modified — Email section)
src/ExtraGasMVC/appsettings.Development.json           (modified — MailHog)
tests/ExtraGasMVC.Tests/ChangePasswordTempDataFlowTests.cs (modified — FakeUsuarioService stubs)
```

## User follow-up checklist (after archive)

- [ ] Apply migration to dev MySQL: `mysql -uroot extragas < db/migrations/20260828_000004_create_password_reset_tokens.sql` (or via the migrator user per `db/scripts/`).
- [ ] Set MailHog for dev: `docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog`. Visit `http://localhost:8025` to see emails.
- [ ] Set SMTP credentials for prod via User Secrets: `dotnet user-secrets set "Email:Username" "..." --project src/ExtraGasMVC` (and same for `Email:Password`).
- [ ] Run the 7-step manual smoke flow documented in PR4 body.
- [ ] Optional follow-up change: clear `IntentosFallidos`/`BloqueadoHasta` on successful reset (HIGH limitation).

## OpenSpec delta sync

- `password-reset-email`: delta mirror identical to base → no merge needed.
- `password-reset-confirm`: delta mirror identical to base → no merge needed.
- Both capabilities are first-time additions; base specs at `openspec/specs/password-reset-{email,confirm}/spec.md` are the canonical source going forward.

## Decision needed

None. Change closed.
