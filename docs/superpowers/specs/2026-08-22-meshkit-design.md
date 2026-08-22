# MeshKit — design

*2026-08-22 · status: approved by default (autonomous `/goal` session; decisions below are stated
assumptions, each with its reason, so they can be overturned cheaply).*

## 1. What MeshKit is

A small SaaS that sells **packs of 3D models**. The models are not hand-made: a **GitHub Actions
pipeline** generates them through the **Meshy API** from prompt files kept in this repository. The
storefront is a **.NET 10 Blazor** web app, payments go through **Stripe Checkout**, and the whole
thing deploys with **one `docker compose up`**.

Three actors, three loops:

| Actor | Loop |
|---|---|
| Philippe (producer) | writes `packs/<slug>.yaml` → dispatches the *Generate pack* workflow → a pack archive appears as a GitHub Release asset and (optionally) inside the running store |
| Buyer | browses packs → previews models in the browser → pays with Stripe → downloads the pack from their library |
| Operator | copies `.env.example` to `.env`, runs `docker compose up -d`, points a Stripe webhook at `/stripe/webhook` |

## 2. Decisions and why

| Decision | Alternative rejected | Why |
|---|---|---|
| Pack **definitions** live in git (`packs/*.yaml`); generated **assets** do not | commit GLBs to the repo | a pack is 50–500 MB of binaries that change on every regeneration; git is the wrong store. Release assets are versioned, free, and addressable |
| The pipeline is a **.NET console tool** (`MeshKit.Pipeline`), the workflow only orchestrates it | shell + `curl` in the workflow | the Meshy flow is stateful (preview → refine, polling, resume, partial failure). That logic needs tests; a workflow YAML can't have them |
| Meshy **preview** output is the free in-browser preview; **refine** output is the paid asset | generate a separate low-poly decoy | the untextured preview is exactly what a buyer needs to judge shape, and it costs nothing extra — it is a mandatory step of the refine flow anyway |
| **SQLite** in a volume | PostgreSQL service in compose | one-person store, <10 writes/minute. One service, one volume, identical engine in tests. Swapping to Npgsql later is a provider change, not a redesign |
| **ASP.NET Core Identity** (email + password), SSR forms | GitHub OAuth (as AtypWebsite) / magic links | buyers aren't necessarily GitHub users; magic links need an SMTP service we don't want in the compose file |
| **Stripe Checkout** (hosted) with inline `price_data` | Payment Element / Products in the Dashboard | zero Dashboard setup beyond keys and a webhook; prices come from the pack manifest |
| Fulfilment **only** from the webhook, idempotent on `session.id` | fulfil on the success page | Stripe's own rule: the buyer may never reach the success page |
| Downloads are zipped **on the fly** from `private/` | pipeline pre-builds `pack.zip` | no duplicate bytes in the catalog; per-model/per-format downloads become trivial later |
| Catalog is a **directory** (`/app/catalog`) mounted as a volume, scanned at startup and on import | catalog in the DB | the pipeline is the source of truth; the DB only holds what the pipeline can't know (users, orders, entitlements) |
| Tailwind v4 via npm, `@google/model-viewer` via npm, built in a node stage of the Dockerfile | CDN scripts / vendored `wwwroot/lib` | a CDN breaks the strict-CSP/self-hosted story; a vendored `dist` is invisible to Renovate (portfolio rule) |

## 3. Repository layout

```
MeshKit/
├── MeshKit.slnx
├── Directory.Build.props          net10.0, nullable, implicit usings, warnings-as-errors off
├── global.json                    10.0.400, rollForward latestFeature
├── .editorconfig                  portfolio template (repo-audit/templates/editorconfig-dotnet)
├── .github/workflows/
│   ├── ci.yml                     build + test on push/PR to main
│   ├── docker.yml                 build + push ghcr.io/phmatray/meshkit on main
│   └── generate-pack.yml          workflow_dispatch(pack, publish) → Meshy → release asset → ingest
├── packs/
│   └── lowpoly-fantasy-props.yaml sample definition
├── src/
│   ├── MeshKit.Core/              domain + manifest + pack definition models (no framework deps)
│   ├── MeshKit.Meshy/             typed HttpClient for api.meshy.ai (preview, refine, poll, download)
│   ├── MeshKit.Pipeline/          console tool: generate · zip · publish
│   └── MeshKit.Web/               Blazor Web App: store, checkout, webhook, library, downloads, ingest
├── tests/
│   ├── MeshKit.Core.Tests/
│   ├── MeshKit.Pipeline.Tests/    covers Meshy client (fake handler) + orchestrator (fake client)
│   └── MeshKit.Web.Tests/         fulfilment, entitlement, catalog import, webhook signature
├── Dockerfile                     node stage (tailwind + model-viewer) → sdk publish → aspnet runtime
├── docker-compose.yml             web (+ optional `stripe-cli` under profile `dev`)
├── .env.example
└── README.md
```

## 4. Domain (`MeshKit.Core`)

### 4.1 Pack definition — `packs/<slug>.yaml` (what the producer writes)

```yaml
slug: lowpoly-fantasy-props
name: Low-Poly Fantasy Props
description: Ten game-ready props in a chunky low-poly style.
price:
  amount: 1900          # minor units
  currency: eur
generation:
  ai_model: latest      # meshy-5 | meshy-6 | meshy-7 | latest
  model_type: lowpoly   # standard | smart-topology | lowpoly
  target_polycount: 5000
  enable_pbr: true
  texture_resolution: 2k
  target_formats: [glb, fbx, obj, usdz]
models:
  - slug: treasure-chest
    name: Treasure Chest
    prompt: a closed wooden treasure chest with iron bands, low poly, game asset
    texture_prompt: weathered oak planks, rusted iron  # optional
```

Validation rules (`PackDefinitionValidator`): slugs are `^[a-z0-9]+(-[a-z0-9]+)*$`, unique within
the pack; prompts ≤ 600 chars (Meshy limit); amount > 0; currency is 3 lowercase letters;
at least one model; `target_formats` ⊆ {glb, fbx, obj, stl, usdz, 3mf} and contains `glb`
(the preview viewer needs it).

### 4.2 Pack manifest — `catalog/<slug>/manifest.json` (what the pipeline writes)

```json
{
  "schemaVersion": 1,
  "slug": "lowpoly-fantasy-props",
  "name": "…", "description": "…",
  "price": { "amount": 1900, "currency": "eur" },
  "generatedAt": "2026-08-22T10:00:00Z",
  "models": [
    {
      "slug": "treasure-chest", "name": "Treasure Chest", "prompt": "…",
      "status": "succeeded",                 // succeeded | failed
      "error": null,
      "previewTaskId": "…", "refineTaskId": "…",
      "thumbnail": "public/thumbs/treasure-chest.png",
      "preview": "public/preview/treasure-chest.glb",
      "files": [
        { "format": "glb", "path": "private/treasure-chest/treasure-chest.glb", "bytes": 1234567 },
        { "format": "fbx", "path": "private/treasure-chest/treasure-chest.fbx", "bytes": 2345678 }
      ],
      "consumedCredits": 30
    }
  ]
}
```

Paths are relative to the pack directory and **must** stay inside it (`public/` or `private/`);
the web app refuses anything else at scan time. A pack is *sellable* when every model is
`succeeded`; otherwise the store hides it (the manifest is still valid — it is the resume state).

### 4.3 Store entities (EF Core, SQLite)

- `ApplicationUser : IdentityUser` — nothing extra.
- `Order` — `Id`, `UserId`, `PackSlug`, `StripeSessionId` (unique), `StripePaymentIntentId?`,
  `AmountTotal`, `Currency`, `Status` (`Pending` | `Paid` | `Failed`), `CreatedAt`, `PaidAt?`.
- `Entitlement` — `Id`, `UserId`, `PackSlug`, `OrderId`, `GrantedAt`; unique on (`UserId`, `PackSlug`).

The pack itself is never in the DB: `PackSlug` is the join key into the catalog.

## 5. Meshy client (`MeshKit.Meshy`)

`IMeshyClient` over `HttpClient` (`BaseAddress = https://api.meshy.ai`, `Authorization: Bearer`):

| Method | Calls |
|---|---|
| `CreatePreviewAsync(PreviewRequest)` | `POST /openapi/v2/text-to-3d` `{mode:"preview", …}` → task id |
| `CreateRefineAsync(RefineRequest)` | `POST /openapi/v2/text-to-3d` `{mode:"refine", preview_task_id, …}` → task id |
| `GetTaskAsync(id)` | `GET /openapi/v2/text-to-3d/{id}` → `MeshyTask` (status, progress, model_urls, thumbnail_url, task_error, consumed_credits) |
| `WaitForTaskAsync(id, pollInterval, timeout, ct)` | polls `GetTaskAsync` until `SUCCEEDED`/`FAILED`/`CANCELED` |
| `DownloadAsync(url, destinationPath)` | streams a signed URL to disk |

Errors: non-2xx → `MeshyApiException(status, body)`; `402` is surfaced as "out of credits" and
aborts the whole run (no point continuing); `429` is retried with backoff (3 attempts).
Download URLs expire, so the orchestrator downloads immediately after `SUCCEEDED`.

## 6. Pipeline (`MeshKit.Pipeline`)

```
meshkit-pipeline generate --pack packs/<slug>.yaml --out catalog [--concurrency 2] [--poll 15] [--timeout 40]
meshkit-pipeline zip      --pack-dir catalog/<slug> --out dist/<slug>.zip
meshkit-pipeline publish  --zip dist/<slug>.zip --url https://store/api/ingest --token $TOKEN
```

`generate` (the `PackGenerator`), per model, with bounded concurrency:

1. If the existing manifest already has the model as `succeeded` and every listed file exists →
   **skip** (this is what makes re-runs resume).
2. Preview task → wait → download `thumbnail_url` → `public/thumbs/<m>.png`, `model_urls.glb` →
   `public/preview/<m>.glb`.
3. Refine task (`preview_task_id`, `enable_pbr`, `texture_resolution`, `target_formats`) → wait →
   download every `model_urls.*` present → `private/<m>/<m>.<ext>` (+ `mtl` next to `obj`).
4. Write the manifest after **every** model (crash-safe resume).

A model that fails is recorded `failed` with the Meshy error and the run continues; exit code is
`1` if any model failed, `0` otherwise. `MESHY_API_KEY` comes from the environment only.

`publish` POSTs the zip as `multipart/form-data` to the store's ingest endpoint with
`Authorization: Bearer <token>`.

## 7. Workflows

- **`generate-pack.yml`** — `workflow_dispatch` with inputs `pack` (slug) and `publish` (bool,
  default false). `timeout-minutes: 330`. Steps: checkout → setup-dotnet → *restore previous
  output* (download release asset `pack-<slug>` if it exists, unzip into `catalog/`, so the run
  resumes) → `generate` → `zip` → upload workflow artifact → create-or-update release
  `pack-<slug>` (tag `pack/<slug>/<run_number>`) with the zip → if `publish` and
  `MESHKIT_INGEST_URL` + `MESHKIT_INGEST_TOKEN` secrets exist → `publish`. Secrets: `MESHY_API_KEY`.
- **`ci.yml`** — `dotnet build` + `dotnet test` on the solution; npm build of the web assets so
  the Tailwind step is exercised.
- **`docker.yml`** — builds the image on `main` and pushes `ghcr.io/phmatray/meshkit:latest` +
  `:sha`.

## 8. Web app (`MeshKit.Web`)

Blazor Web App, static SSR everywhere (no circuit needed: the only client-side piece is the
`<model-viewer>` web component). Tailwind v4 for styling, semantic HTML, WCAG AA.

| Route | What |
|---|---|
| `/` | hero + sellable packs |
| `/packs` | all sellable packs |
| `/packs/{slug}` | description, price, model grid (thumbnail), 3D preview of the selected model, **Buy** (or **Download** if owned) |
| `POST /checkout/{slug}` | creates the Checkout Session (auth required), redirects to Stripe |
| `/checkout/success`, `/checkout/cancel` | informational only |
| `/library` | the user's entitled packs with download buttons |
| `GET /library/{slug}/download` | entitlement check → streams a zip of `private/` |
| `/account/register`, `/account/login`, `/account/logout` | Identity, SSR forms |
| `POST /stripe/webhook` | signature-verified event handler |
| `POST /api/ingest` | bearer-token-protected pack import (zip → `catalog/<slug>`, rescan) |
| `/catalog/{slug}/public/{**path}` | static thumbnails + preview GLBs (public by design) |
| `/health` | liveness |

**Checkout session**: `mode=payment`, `line_items[0].price_data = {currency, unit_amount,
product_data.name, product_data.images[thumbnail]}`, `client_reference_id = userId`,
`customer_email`, `metadata.pack_slug`, `success_url`/`cancel_url` from `MeshKit:PublicBaseUrl`,
`integration_identifier = "meshkit-pack-checkout-<8 letters>"`. No `payment_method_types`. An
`Order(Pending)` row is written before redirecting, keyed by the session id.

**Webhook** (`StripeWebhookEndpoint` → `FulfillmentService`):

- `checkout.session.completed` / `checkout.session.async_payment_succeeded`: if
  `payment_status == "paid"` → `Fulfill(session)`: find-or-create the Order by session id, set
  `Paid`, create the Entitlement if absent. Idempotent — replaying the event is a no-op.
- `checkout.session.async_payment_failed` → Order `Failed`.
- Anything else → 200, ignored. Bad signature → 400.

**Catalog** (`CatalogService`): scans `MeshKit:Catalog:Path` at startup; `Reload()` after import;
exposes `GetSellable()`, `Get(slug)`, `OpenPrivateZip(slug)`. Path traversal in a manifest ⇒ the pack
is skipped with a logged error, never served.

**Ingest** (`POST /api/ingest`): refuses without `MeshKit:Ingest:Token` configured, constant-time
compares the bearer, extracts to a temp dir, validates the manifest, atomically swaps it into
`catalog/<slug>`, reloads.

## 9. Deployment

`docker-compose.yml`: one `web` service (image `ghcr.io/phmatray/meshkit`, or `build: .`),
port `8080`, volumes `./data:/app/data` (SQLite + DataProtection keys) and `./catalog:/app/catalog`,
healthcheck on `/health`, env from `.env`. A `stripe-cli` service under `profiles: [dev]` forwards
webhooks to `web:8080/stripe/webhook` for local testing. TLS termination is the operator's reverse
proxy (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`), exactly like AtypWebsite.

`.env.example` documents: `STRIPE__SECRETKEY` (a restricted key), `STRIPE__WEBHOOKSECRET`,
`MESHKIT__PUBLICBASEURL`, `MESHKIT__INGEST__TOKEN`.

Migrations run at startup (`Database.Migrate()`) — acceptable for a single-instance SQLite store.

## 10. Testing

- **Core**: definition validation (each rule has a failing case), manifest round-trip, path-safety.
- **Pipeline**: Meshy client against a fake `HttpMessageHandler` (request shape, bearer header,
  402 abort, 429 retry); `PackGenerator` against a fake `IMeshyClient` (happy path, resume skips
  completed models, failure recorded without aborting others, manifest written after each model).
- **Web**: `FulfillmentService` idempotency on a SQLite in-memory DbContext; unpaid session not
  fulfilled; `async_payment_failed` marks Failed; download refused without entitlement; catalog
  rejects traversal; ingest refuses bad token; webhook endpoint rejects bad signature and accepts a
  signature produced with `EventUtility.ComputeSignature` (via `WebApplicationFactory`).

## 11. Out of scope (deliberately)

Subscriptions, Stripe Tax (requires a registration — not assumed), per-model purchases, admin UI,
email notifications, multiple currencies per pack, CDN for downloads, refunds UI.
