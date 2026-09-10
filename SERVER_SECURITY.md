# VPS Security - Cloudflare Only Web Traffic

## Goal

Allow:

- SSH `22` → trusted admin IP only
- HTTP `80` → Cloudflare IP ranges only
- HTTPS `443` → Cloudflare IP ranges only

Block:

- Direct access to `SERVER_IP:80`
- Direct access to `SERVER_IP:443`
- PostgreSQL `5432`
- Redis `6379`
- Classifier `50051`
- Next.js `3000`
- API `5000`

Architecture:

User
  ↓
Cloudflare
  ↓
VPS :80 / :443
  ↓
Docker Nginx
  ├── Next.js
  └── API

---

# 1. Cloudflare IPv4 ranges

Current official Cloudflare IPv4 networks:

173.245.48.0/20
103.21.244.0/22
103.22.200.0/22
103.31.4.0/22
141.101.64.0/18
108.162.192.0/18
190.93.240.0/20
188.114.96.0/20
197.234.240.0/22
198.41.128.0/17
162.158.0.0/15
104.16.0.0/13
104.24.0.0/14
172.64.0.0/13
131.0.72.0/22

Official source:

https://www.cloudflare.com/ips-v4/

---

# 2. Cloudflare IPv6 ranges

If the VPS uses public IPv6:

2400:cb00::/32
2606:4700::/32
2803:f800::/32
2405:b500::/32
2405:8100::/32
2a06:98c0::/29
2c0f:f248::/32

Official source:

https://www.cloudflare.com/ips-v6/

---

# 3. Recommended: VPS Provider Firewall

If the VPS provider has a Network Firewall / VPS Firewall,
configure the rules there.

## SSH

Allow:

TCP 22
Source: YOUR_PUBLIC_IP/32

Example:

TCP 22
Source: 1.2.3.4/32

Do NOT use:

TCP 22
Source: 0.0.0.0/0

unless necessary.

---

## HTTP

Create an ALLOW rule for TCP port 80 for EACH Cloudflare network.

Example:

ALLOW TCP 80 from 173.245.48.0/20
ALLOW TCP 80 from 103.21.244.0/22
ALLOW TCP 80 from 103.22.200.0/22
...

After all Cloudflare rules:

DENY TCP 80 from 0.0.0.0/0

---

## HTTPS

Create an ALLOW rule for TCP port 443 for EACH Cloudflare network.

Example:

ALLOW TCP 443 from 173.245.48.0/20
ALLOW TCP 443 from 103.21.244.0/22
ALLOW TCP 443 from 103.22.200.0/22
...

After all Cloudflare rules:

DENY TCP 443 from 0.0.0.0/0

---

# 4. Expected Firewall

The final concept should be:

22:
    YOUR_IP       → ALLOW
    everyone else → DENY

80:
    Cloudflare    → ALLOW
    everyone else → DENY

443:
    Cloudflare    → ALLOW
    everyone else → DENY

All other inbound ports:
    DENY

---

# 5. Docker

Nginx is the ONLY container that should publish public ports.

docker-compose.infra.yml (Nginx/PostgreSQL/Redis/RabbitMQ) and docker-compose.yml (application services); see [INFRASTRUCTURE.md](INFRASTRUCTURE.md) for the shared networks:

services:

  nginx:
    ports:
      - "80:8080"
      - "443:8443"

  web:
    expose:
      - "3000"

  api:
    expose:
      - "5000"

  postgres:
    expose:
      - "5432"

  redis:
    expose:
      - "6379"

  classifier:
    expose:
      - "50051"

Do NOT publish:

3000:3000
5000:5000
5432:5432
6379:6379
50051:50051

These services should only be reachable through the Docker
internal network.

---

# 6. Important: Docker + UFW

Do NOT rely only on UFW for Docker published ports.

Docker creates its own firewall/NAT rules and published container
ports may bypass normal UFW filtering.

Preferred:

VPS Provider Firewall
        ↓
Docker
        ↓
Nginx

If no provider firewall is available, configure the Docker
DOCKER-USER firewall chain instead.

---

# 7. Check exposed ports

Run:

sudo docker ps

Expected nginx ports:

0.0.0.0:80->8080/tcp
0.0.0.0:443->8443/tcp

Other containers should NOT show public mappings.

Also check:

sudo ss -lntp

Public services should normally only be:

22
80
443

---

# 8. Cloudflare configuration

Cloudflare DNS:

A
Name: @
Content: VPS_IP
Proxy: Proxied (orange cloud)

CNAME
Name: www
Target: dooduang.cc
Proxy: Proxied (orange cloud)

SSL/TLS mode:

Full (strict)

---

# 9. Test Cloudflare

Check DNS:

nslookup dooduang.cc

It should return Cloudflare IP addresses instead of the VPS IP.

Website:

curl -I https://dooduang.cc

Should work.

---

# 10. Test Direct IP

Try:

curl -k -I https://YOUR_VPS_IP

or:

curl -I http://YOUR_VPS_IP

After the firewall is configured correctly,
the direct connection should timeout or be rejected.

But:

curl -I https://dooduang.cc

must still work.

---

# 11. Final Architecture

Internet
   │
   X────────────→ VPS_IP:443
   │              BLOCKED
   │
   ▼
Cloudflare
   │
   │ Allowed Cloudflare IP
   ▼
VPS :443
   │
   ▼
Docker Nginx
   │
   ├── web:3000
   │
   └── api:5000
          │
          ├── postgres:5432
          ├── redis:6379
          └── classifier:50051

Public ports:

22  → Admin only
80  → Cloudflare only
443 → Cloudflare only

sudo ufw default deny incoming && \
sudo ufw default allow outgoing && \
sudo ufw allow from 173.245.48.0/20 to any port 80 proto tcp && \
sudo ufw allow from 173.245.48.0/20 to any port 443 proto tcp && \
sudo ufw allow from 103.21.244.0/22 to any port 80 proto tcp && \
sudo ufw allow from 103.21.244.0/22 to any port 443 proto tcp && \
sudo ufw allow from 103.22.200.0/22 to any port 80 proto tcp && \
sudo ufw allow from 103.22.200.0/22 to any port 443 proto tcp && \
sudo ufw allow from 103.31.4.0/22 to any port 80 proto tcp && \
sudo ufw allow from 103.31.4.0/22 to any port 443 proto tcp && \
sudo ufw allow from 141.101.64.0/18 to any port 80 proto tcp && \
sudo ufw allow from 141.101.64.0/18 to any port 443 proto tcp && \
sudo ufw allow from 108.162.192.0/18 to any port 80 proto tcp && \
sudo ufw allow from 108.162.192.0/18 to any port 443 proto tcp && \
sudo ufw allow from 190.93.240.0/20 to any port 80 proto tcp && \
sudo ufw allow from 190.93.240.0/20 to any port 443 proto tcp && \
sudo ufw allow from 188.114.96.0/20 to any port 80 proto tcp && \
sudo ufw allow from 188.114.96.0/20 to any port 443 proto tcp && \
sudo ufw allow from 197.234.240.0/22 to any port 80 proto tcp && \
sudo ufw allow from 197.234.240.0/22 to any port 443 proto tcp && \
sudo ufw allow from 198.41.128.0/17 to any port 80 proto tcp && \
sudo ufw allow from 198.41.128.0/17 to any port 443 proto tcp && \
sudo ufw allow from 162.158.0.0/15 to any port 80 proto tcp && \
sudo ufw allow from 162.158.0.0/15 to any port 443 proto tcp && \
sudo ufw allow from 104.16.0.0/13 to any port 80 proto tcp && \
sudo ufw allow from 104.16.0.0/13 to any port 443 proto tcp && \
sudo ufw allow from 104.24.0.0/14 to any port 80 proto tcp && \
sudo ufw allow from 104.24.0.0/14 to any port 443 proto tcp && \
sudo ufw allow from 172.64.0.0/13 to any port 80 proto tcp && \
sudo ufw allow from 172.64.0.0/13 to any port 443 proto tcp && \
sudo ufw allow from 131.0.72.0/22 to any port 80 proto tcp && \
sudo ufw allow from 131.0.72.0/22 to any port 443 proto tcp

IPV6
sudo ufw allow from 2400:cb00::/32 to any port 80 proto tcp && \
sudo ufw allow from 2400:cb00::/32 to any port 443 proto tcp && \
sudo ufw allow from 2606:4700::/32 to any port 80 proto tcp && \
sudo ufw allow from 2606:4700::/32 to any port 443 proto tcp && \
sudo ufw allow from 2803:f800::/32 to any port 80 proto tcp && \
sudo ufw allow from 2803:f800::/32 to any port 443 proto tcp && \
sudo ufw allow from 2405:b500::/32 to any port 80 proto tcp && \
sudo ufw allow from 2405:b500::/32 to any port 443 proto tcp && \
sudo ufw allow from 2405:8100::/32 to any port 80 proto tcp && \
sudo ufw allow from 2405:8100::/32 to any port 443 proto tcp && \
sudo ufw allow from 2a06:98c0::/29 to any port 80 proto tcp && \
sudo ufw allow from 2a06:98c0::/29 to any port 443 proto tcp && \
sudo ufw allow from 2c0f:f248::/32 to any port 80 proto tcp && \
sudo ufw allow from 2c0f:f248::/32 to any port 443 proto tcp
