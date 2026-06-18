# stashr Web UI — Manual Test Plan (v0.9.0)

A pre-release acceptance checklist for the built-in admin console served at `/ui`. Work through
each section; mark **Pass / Fail** and note anything unexpected. Anything in **bold "Expected"**
is the pass criterion.

## How to use this document

- Run a fresh server so state is clean (in-memory dev resets on restart).
- Test in at least one Chromium browser and one of Firefox/Safari.
- Re-run the whole sheet once in **light** theme and once in **dark** theme.
- For each row: `[ ]` not run · `[P]` pass · `[F]` fail (add a note).

## Environment setup

| # | Step | Expected |
|---|------|----------|
| ENV-1 | Start the stack: `docker compose up --build` (or `dotnet run --project src/Stashr.Server`). | Server starts; logs show "Now listening on …". |
| ENV-2 | Note the API base URL (Docker: `http://localhost:8080`, source: `http://localhost:5000`). | — |
| ENV-3 | Open `<base>/ui` in a browser. | The console loads (no blank page, no console errors). |
| ENV-4 | Copy the one-time **root token** from the server log (dev mode auto-inits). | Token available for login tests. |

> If you start in production mode (`DevMode=false`, no auto-unseal), the server is uninitialized —
> use the **Initialize & Unseal** section first; it returns the root token in the UI.

---

## 1. Initialize & unseal

Run these against a **fresh, uninitialized** instance (production mode or first run). Skip the init
rows if dev mode already auto-initialized — then the app goes straight to login.

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| INIT-1 | First-run gate | Open `/ui` on an uninitialized server. | The **Initialize** screen shows (not login), with key-shares and threshold fields. |
| INIT-2 | Validation | Set shares = 5, threshold = 3, click **Initialize**. | Success; a one-time screen shows the **root token** and **5 unseal keys**. |
| INIT-3 | Copy buttons | Click **Copy** on the root token and on a key. | Value copied to clipboard (paste elsewhere to confirm). |
| INIT-4 | Continue | Click **I've saved them — continue**. | App proceeds; if unsealed, lands on the dashboard signed in as root; if sealed, shows the unseal screen. |
| UNSEAL-1 | Sealed gate | Open `/ui` on an initialized-but-sealed server. | The **Unseal** screen shows with a progress bar (0 / threshold). |
| UNSEAL-2 | Partial progress | Enter one valid unseal key, submit. | Progress advances (e.g. 1 / 3); input clears; no error. |
| UNSEAL-3 | Reach threshold | Enter the remaining keys until threshold met. | App unseals and advances to login (or dashboard if a token is already held). |
| UNSEAL-4 | Bad key | Enter a wrong/garbage key. | A clear error is shown; progress does **not** advance. |
| UNSEAL-5 | Enter-to-submit | Type a key and press **Enter**. | Submits the key (same as clicking the button). |

---

## 2. Authentication

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| AUTH-1 | Login screen | Reach the app while unsealed and signed out. | The **Sign in** screen shows with **Token** and **AppRole** tabs. |
| AUTH-2 | Token login (valid) | On Token tab, paste the root token, click **Sign in**. | Signed in; the dashboard loads. |
| AUTH-3 | Token login (invalid) | Paste a random string, sign in. | Error "That token was rejected."; stays on login. |
| AUTH-4 | Empty token | Click **Sign in** with the field empty. | Inline "Enter a token." message; no request error. |
| AUTH-5 | AppRole login | Create a role + secret-id first (see Access), switch to AppRole tab, enter role_id + secret_id, sign in. | Signed in with that role's token. |
| AUTH-6 | AppRole login (bad) | Enter wrong role_id/secret_id. | Clear failure message; stays on login. |
| AUTH-7 | Enter-to-submit | Token tab, type token, press **Enter**. | Submits login. |

---

## 3. Theme & app shell

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| UI-THEME-1 | Toggle | Click the sun/moon button in the top bar. | Theme switches light ↔ dark instantly; sidebar stays dark navy in both. |
| UI-THEME-2 | Persistence | Toggle to dark, refresh the page. | Loads in dark with **no white flash** before render. |
| UI-THEME-3 | System default | Clear site data, set OS to dark, open `/ui`. | First load respects the OS preference. |
| UI-SHELL-1 | Navigation | Click each sidebar item (Dashboard, KV, Transit, Mounts, Policies, Auth & tokens, Leases, Audit). | Each route loads its page; active item is highlighted. |
| UI-SHELL-2 | Deep link refresh | Navigate to `/ui/policies`, refresh. | The Policies page reloads correctly (SPA fallback works). |
| UI-SHELL-3 | Brand mark | Observe the sidebar logo and the browser tab favicon. | Both show the stashr mark; tab title reads "stashr". |
| UI-SHELL-4 | Seal pill | Observe the top bar. | An "Unsealed" pill is shown; a standby/HA role pill appears only when applicable. |

---

## 4. Dashboard

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| DASH-1 | Load | Open Dashboard. | Four stat cards render: Seal status, Version, Mounted engines, Audit chain. |
| DASH-2 | Live values | Compare to the API. | Version matches `/v1/sys/health`; mounted-engines count matches Mounts page; audit shows "Intact". |
| DASH-3 | Quick actions | Click each quick-action button. | Navigates to Secrets / Policies / Access / Audit respectively. |
| DASH-4 | Pre-audit notice | Read the "About this build" card. | Shows the pre-audit (v0.9.0) warning. |

---

## 5. KV secrets

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| KV-1 | Empty list | Open **KV secrets** on a fresh instance. | Breadcrumb shows `secret`; list shows "No secrets here." (no error). |
| KV-2 | Create secret | Click **New secret**, path `app/db`, add `password=p@ss`, `host=db1`, **Save**. | Saves; the secret appears and opens with version 1. |
| KV-3 | Masked values | View `app/db`. | Values show as `••••••••` until revealed. |
| KV-4 | Reveal / hide | Click **Reveal** then **Hide** on a row. | Value toggles between plaintext and masked. |
| KV-5 | Copy value | Click **Copy** on a row. | Value copied to clipboard. |
| KV-6 | Folder navigation | Create `app/api/key1=abc`; navigate into `app/` then `api/`. | Folders (trailing `/`) navigate; breadcrumb updates; leaves open on click. |
| KV-7 | Breadcrumb up | Click `secret` / a parent crumb. | Returns to that prefix and re-lists. |
| KV-8 | Edit (new version) | Open `app/db`, **Edit**, change a value, add a field, **Save**. | Saves; re-opens at the new version with updated data. |
| KV-9 | Remove field | In edit mode, remove a row with **✕**, save. | The removed key no longer appears. |
| KV-10 | Delete | Open a secret, **Delete**. | Secret is soft-deleted; selection clears; list refreshes. |
| KV-11 | Empty path guard | Ensure listing the root prefix works. | No "Required parameter path" / 400 error (regression check). |

---

## 6. Policies & explain-access

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| POL-1 | List | Open **Policies**. | Existing policy names list on the left (at least `root`/`default` if seeded). |
| POL-2 | Create | **New policy**, name `app-read`, rule path `secret/data/app/*`, check `read` + `list`, **Save**. | Saves; `app-read` appears in the list. |
| POL-3 | Reopen | Select `app-read`. | Rules and checked capabilities load exactly as saved. |
| POL-4 | Multi-rule | Add a second rule, save, reopen. | Both rules persist with their capabilities. |
| POL-5 | Explain (allow) | In Explain access: path `secret/data/app/db`, capability `read`, policies `app-read`, **Explain**. | Result **Allowed** with an explanation naming the winning rule/policy. |
| POL-6 | Explain (deny) | Path `secret/data/other`, capability `read`, policies `app-read`, **Explain**. | Result **Denied** with a reason. |
| POL-7 | Explain (no policy) | Leave policies blank, explain a path. | Denied (deny-by-default) with explanation. |

---

## 7. Auth & tokens (AppRole)

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| APP-1 | Create role | Name `payments-api`, policies `app-read`, TTL 3600, **Create role**. | Returns and displays a **role_id** (copyable). |
| APP-2 | Look up role ID | In Issue secret ID, role `payments-api`, **Get role ID**. | Shows the same role_id. |
| APP-3 | Generate secret ID | **Generate secret ID** for `payments-api`. | Shows a **secret_id** (copyable). |
| APP-4 | Wrapped secret ID | Set Wrap TTL = 120, **Generate secret ID**. | Returns a single-use **wrapping token** instead of the raw secret_id. |
| APP-5 | Unknown role | Use a role name that doesn't exist. | Clear "Role not found." error, no crash. |
| APP-6 | End-to-end | Use APP-1/APP-3 values to log in via the AppRole tab (AUTH-5). | Login succeeds. |

---

## 8. Transit

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| TR-1 | Create key | Keys section, name `orders`, **Create key**. | Confirmation pill "key 'orders' ready". |
| TR-2 | Encrypt | Encrypt panel: key `orders`, plaintext `hello world`, **Encrypt**. | Returns ciphertext starting `stashr:v1:` (copyable). |
| TR-3 | Decrypt round-trip | Decrypt panel: key `orders`, paste the ciphertext, **Decrypt**. | Plaintext reads back exactly `hello world`. |
| TR-4 | Wrong key | Decrypt the ciphertext with a different/nonexistent key. | Clear error (key not found / bad ciphertext); no crash. |
| TR-5 | Unicode | Encrypt/decrypt a string with accents/emoji. | Round-trips correctly (UTF-8 preserved). |

---

## 9. Leases

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| LEASE-1 | Empty | Open **Leases** with no dynamic creds issued. | "No active leases." (no error). |
| LEASE-2 | Populate | If a database engine is configured, request creds via API, then **Refresh**. | The lease appears in the table. |
| LEASE-3 | Revoke | Click **Revoke** on a lease. | Lease is revoked and disappears after refresh. |
| LEASE-4 | Refresh | Click **Refresh**. | List reloads without full-page reload. |

---

## 10. Audit

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| AUD-1 | Verify | Open **Audit log**, click **Verify now**. | Shows **Intact**, a checked-entry count, and "—" for first broken sequence. |
| AUD-2 | Repeat | After performing several KV writes, verify again. | Checked count increases; still Intact. |

---

## 11. Mounts

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| MNT-1 | List | Open **Mounts**. | Table lists mounted engines with path + type (e.g. `secret/` kv, `transit/` transit, `cubbyhole/` …). |
| MNT-2 | Refresh | Click **Refresh**. | Reloads the list. |

---

## 12. Session & sign-out

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| SES-1 | Persist on refresh | Signed in, refresh the page. | Stays signed in (session token survives refresh). |
| SES-2 | Sign out | Click the sign-out icon in the top bar. | Returns to the login screen. |
| SES-3 | New tab close | Sign in, close the tab, reopen `/ui`. | Requires sign-in again (sessionStorage cleared on tab close). |

---

## 13. Negative & resilience

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| NEG-1 | Server down | Stop the server, open `/ui`. | A clear "Couldn't reach the stashr API" screen with a **Retry** button. |
| NEG-2 | Recover | Restart the server, click **Retry**. | App proceeds (unseal/login/dashboard as appropriate). |
| NEG-3 | Sealed mid-session | Seal the engine via API while using the app, then act. | Operations surface a "sealed" error rather than crashing. |
| NEG-4 | Forbidden token | Log in with a low-privilege token, open Policies. | A clear "permission denied" message, not a blank page. |
| NEG-5 | Console clean | With dev tools open, exercise main flows. | No uncaught JS exceptions in the console. |

---

## 14. Cross-cutting

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| X-1 | Responsive | Narrow the window to ~700px and below. | Layout adapts (KV/policy panes stack); no horizontal scroll or clipped controls. |
| X-2 | No external calls | With dev tools Network open, load the console. | No requests to third-party origins (fonts/CDNs) — only same-origin and `/v1`. |
| X-3 | Both themes | Re-run sections 4–11 in the other theme. | All text remains legible; no low-contrast or invisible elements. |
| X-4 | Browser matrix | Repeat smoke tests in a second browser. | Behaviour consistent across browsers. |

---

## Sign-off

| Area | Result | Tester | Date | Notes |
|------|--------|--------|------|-------|
| Init & unseal | | | | |
| Authentication | | | | |
| Theme & shell | | | | |
| Dashboard | | | | |
| KV secrets | | | | |
| Policies & explain | | | | |
| Auth & tokens | | | | |
| Transit | | | | |
| Leases | | | | |
| Audit | | | | |
| Mounts | | | | |
| Session | | | | |
| Negative & resilience | | | | |
| Cross-cutting | | | | |

**Release gate:** all sections Pass (or every Fail triaged and accepted) before tagging `v0.9.0`.
