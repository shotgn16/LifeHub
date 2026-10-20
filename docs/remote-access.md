# Remote access & HTTPS

There are three ways to reach LifeHub:

| Route | Login | HTTPS | Good for |
|---|---|---|---|
| **HA sidebar / HA app** (ingress) | Home Assistant's | Same as your HA | Everyday use when you already use the HA app |
| **Direct port** `http://<ha-ip>:8099` | LifeHub login | No | Quick access on your home network |
| **Own hostname** (Cloudflare Tunnel or reverse proxy) | LifeHub login | Yes | Phones away from home, and the live barcode scanner |

> **The live camera scanner needs HTTPS.** Phones only allow camera access on secure pages. Over
> plain `http://` you can still use **Take photo**, which reads the barcode from a picture.

## 1. Set a LifeHub login first

Opened through HA, LifeHub trusts Home Assistant's sign-in. Any other route uses LifeHub's own
login, so create one in **Settings → Security** before opening the port.

## 2. Open the direct port (HA add-on)

Go to **Settings → Add-ons → LifeHub → Configuration → Network**. Set `8099/tcp` to a host port
(for example `8099`), then save and restart the add-on. LifeHub is now at `http://<ha-ip>:8099`.

## 3. Cloudflare Tunnel

If you already use a Cloudflare Tunnel for Home Assistant, add LifeHub as another hostname on the
same tunnel.

1. Open the Cloudflare **Zero Trust** dashboard. Go to **Networks → Tunnels**, select your tunnel,
   then **Public hostnames** (or **Published application routes**), then **Add**.
2. Fill in the route:
   - **Subdomain:** `lifehub`
   - **Domain:** your domain
   - **Path:** empty
   - **Service type:** `HTTP`
   - **URL:** `<ha-ip>:8099` (for example `192.168.1.10:8099`)
3. Save. DNS is created automatically. Open `https://lifehub.<your-domain>`.

Notes:

- Use the **IP and port**, not `localhost` or `homeassistant:8099`. The `cloudflared` connector
  usually runs on another machine or container, where those names don't point at HA.
- Hostnames don't answer `ping` through Cloudflare. Test in a browser instead.
- WebSockets work through Cloudflare out of the box.
- In LifeHub, set **Settings → General → LifeHub web address** to `https://lifehub.<your-domain>`.
  Phone notifications then open LifeHub directly when tapped.
- You can add **Cloudflare Access** in front for an extra sign-in layer.

## 4. Other reverse proxies

Nginx, Caddy, Traefik or Nginx Proxy Manager all work. Forward everything to port 8099 with
WebSocket upgrade enabled, and pass `X-Forwarded-Proto` and `X-Forwarded-For`. LifeHub honours
forwarded headers so that redirects, cookies and the scanner work behind HTTPS. A full nginx
example is in [installation.md](installation.md#option-a-plesk-on-linux-with-the-docker-extension-recommended).

Caddy example:

```caddyfile
lifehub.example.com {
    reverse_proxy 192.168.1.10:8099
}
```
