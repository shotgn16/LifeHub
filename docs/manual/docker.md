# Running LifeHub in Docker

Docker runs LifeHub in a container. Your data lives in a Docker volume called `lifehub-data`.
The image is **`ghcr.io/shotgn16/lifehub`** (for 64-bit PCs and ARM boards like the Raspberry Pi).

## The easy way: from the LifeHub app

If you've installed the LifeHub app on [Windows](windows.md), a [Mac](mac.md) or [Linux](linux.md):

1. Install **Docker** first:
   - Windows or Mac: [Docker Desktop](https://www.docker.com/products/docker-desktop/), then open
     it once so it's running.
   - Linux: [Docker Engine](https://docs.docker.com/engine/install/).
2. In LifeHub's setup assistant choose **Docker**, check the port (8099) and press **Install and
   start**. LifeHub downloads the image, starts the container (it restarts automatically with
   Docker) and opens it.
3. LifeHub in Docker asks you to **create your account**.

From then on the LifeHub app opens your Docker LifeHub, and starts the container if it's stopped.

## By hand

```bash
docker run -d --name lifehub --restart unless-stopped \
  -p 8099:8099 -v lifehub-data:/data -e TZ=Europe/London \
  ghcr.io/shotgn16/lifehub:latest
```

Open `http://localhost:8099` (or `http://<this-computer's-IP>:8099` from another device) and create
your account. Change `TZ` to your time zone.

### Docker Compose

```yaml
services:
  lifehub:
    image: ghcr.io/shotgn16/lifehub:latest
    container_name: lifehub
    restart: unless-stopped
    ports: ["8099:8099"]
    environment:
      TZ: Europe/London
    volumes:
      - lifehub-data:/data
volumes:
  lifehub-data:
```

`docker compose up -d`. To build from the source instead, use the `docker-compose.yml` in this
repository (`docker compose up -d --build`).

## Updating

```bash
docker pull ghcr.io/shotgn16/lifehub:latest
docker rm -f lifehub
# then the same docker run command as above — your data stays in the lifehub-data volume
```

Or press **Install and start** again in the app's setup assistant.

## Good to know

- **AI:** Ollama on the same computer is at `http://host.docker.internal:11434` from inside the
  container (Docker Desktop). On Linux use the computer's IP address, and start Ollama with
  `OLLAMA_HOST=0.0.0.0`. Set it in **Settings → AI**.
- **Backups:** made every night inside the volume (`/data/backups`). Copy one out with
  `docker cp lifehub:/data/backups ./lifehub-backups`.
- **HTTPS** (for the phone camera scanner and access from outside): see
  [Remote access](../remote-access.md).
