# Deployment — production

**https://meshkit.atypical.consulting** runs on the Atypical Consulting VPS (`root@62.72.19.224`,
Ubuntu 24.04, shared with AtypWebsite and key3s), as a Docker container behind the host's nginx.

| What | Where |
|---|---|
| Compose project | `/opt/meshkit/docker-compose.yml` (copy of this repo's), `.env` (secrets, mode 600) |
| Image | `ghcr.io/phmatray/meshkit:latest`, built by `.github/workflows/docker.yml` on every push to `main` |
| Container | `meshkit`, bound to `127.0.0.1:5102` only (`MESHKIT_PORT=127.0.0.1:5102` in `.env`) |
| Data | `/opt/meshkit/data` — SQLite (`meshkit.db`) + DataProtection keys; owned by uid 1001 |
| Catalog | `/opt/meshkit/catalog/<slug>/` — filled by `POST /api/ingest`; owned by uid 1001 |
| nginx | `/etc/nginx/sites-available/meshkit` (symlinked in `sites-enabled`), `client_max_body_size 2g`, `proxy_request_buffering off` |
| TLS | Let's Encrypt via `certbot --nginx`, renewed by the `certbot.timer` already on the host |
| Logs | `docker logs meshkit`, `/var/log/nginx/{access,error}.log` |

## Routine operations

```bash
# Upgrade to the latest image (after a push to main turned the Docker workflow green)
ssh root@62.72.19.224 'cd /opt/meshkit && docker compose pull -q && docker compose up -d && sleep 8 && docker compose ps'

# Publish a pack: the "Generate pack" workflow with publish=true does it (secrets MESHKIT_INGEST_URL/TOKEN).
# By hand, from a release asset:
ssh root@62.72.19.224 "cd /tmp && curl -sSL -o p.zip https://github.com/phmatray/MeshKit/releases/download/pack%2F<slug>%2F<n>/<slug>.zip \
  && curl -s -w '\n%{http_code}\n' -H \"Authorization: Bearer \$(grep ^MESHKIT__INGEST__TOKEN= /opt/meshkit/.env | cut -d= -f2)\" -F 'file=@p.zip;type=application/zip' http://127.0.0.1:5102/api/ingest; rm -f p.zip"

# Health
curl -s https://meshkit.atypical.consulting/health
```

## Stripe (live)

- `STRIPE__SECRETKEY` in `/opt/meshkit/.env`: a **restricted** key, permission *Checkout Sessions: Write*.
- Webhook endpoint `https://meshkit.atypical.consulting/stripe/webhook`, events
  `checkout.session.completed`, `checkout.session.async_payment_succeeded`,
  `checkout.session.async_payment_failed`; its signing secret goes in `STRIPE__WEBHOOKSECRET`.
- After editing `.env`: `docker compose up -d` (recreates the container with the new environment).

## Email (transactional)

`SMTP__*` in `/opt/meshkit/.env` — Infomaniak (`mail.infomaniak.com:587`, STARTTLS), same mailbox as
AtypWebsite (`philippe@atypical.consulting`), From "MeshKit by Atypical Consulting". Sends purchase
confirmations, address confirmations and password resets; delivery is logged as
`Email sent to <addr>: <subject>` / `Email to <addr> failed after 3 attempts`. With `SMTP__HOST` empty
the app logs instead of sending.

## First-time setup (what was done on 2026-08-23, for the record)

```bash
mkdir -p /opt/meshkit/{data,catalog} && chown -R 1001:1001 /opt/meshkit/{data,catalog}
# docker-compose.yml + .env copied in; docker compose pull && up -d
# nginx vhost written, `nginx -t && systemctl reload nginx`
# DNS A meshkit.atypical.consulting → 62.72.19.224 (Infomaniak), then:
certbot --nginx -d meshkit.atypical.consulting --redirect -m philippe@atypical.consulting --agree-tos -n
```
