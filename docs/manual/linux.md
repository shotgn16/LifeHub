# Installing LifeHub on Linux

Works on 64-bit PCs (`amd64`) and ARM boards such as the Raspberry Pi 4/5 (`arm64`), with a
desktop (GNOME, KDE, Xfce, Cinnamon…) and WebKitGTK 4.1 — Ubuntu 22.04 or later, Debian 12
or later, Fedora 39 or later, or a current Arch/openSUSE. For a server **without** a desktop, use
[Docker](docker.md) or the portable server build (see [installation.md](../installation.md)).

Not sure which you have? Open a terminal and type `uname -m`: `x86_64` → **amd64**,
`aarch64` → **arm64**.

## Ubuntu, Debian, Linux Mint, Pop!_OS, Raspberry Pi OS (.deb)

1. Download **`LifeHub-<version>-Linux-amd64.deb`** (or `arm64`) from the
   [Releases page](https://github.com/shotgn16/LifeHub/releases/latest).
2. Double-click it and press **Install** (your software centre opens it). Or in a terminal:

   ```bash
   sudo apt install ./LifeHub-*-Linux-*.deb
   ```

   This also installs what LifeHub needs (WebKitGTK, which draws the window).
3. Open **LifeHub** from your applications menu.

## Fedora, Arch, openSUSE and others (.tar.gz)

1. Download **`LifeHub-<version>-Linux-amd64.tar.gz`** (or `arm64`).
2. In a terminal:

   ```bash
   tar xzf LifeHub-*-Linux-*.tar.gz
   cd LifeHub-*-Linux-*/
   ./install.sh
   ```

   It installs LifeHub for your user (no root), adds it to your applications menu, and asks for
   your password only if WebKitGTK needs installing. Then it opens LifeHub.

## Set it up

The **setup assistant** opens on first start:

1. **How do you want to use LifeHub?** This computer only · this computer + my phone · a
   household server · Docker · Home Assistant · LifeHub somewhere else.
   - The phone and household options run LifeHub as a **systemd user service**. For *When the
     computer starts*, LifeHub asks for your password to turn on "lingering", so it runs even
     before you sign in.
   - Using a firewall (`ufw`)? Allow the port: `sudo ufw allow 8099/tcp`.
2. **Create your account.**
3. **The AI assistant**: **Yes, install Ollama** runs Ollama's official installer (it asks for your
   password), then downloads a model that fits your computer. Or **No thanks** for later
   ([The AI assistant](ai.md)).
4. **Connect your phone**: see [Phone apps](phone-apps.md).
5. **Open LifeHub.**

## Every day

- Open LifeHub from the applications menu, or type `lifehub`. Right-click its icon for **Setup
  assistant**, or run `lifehub --setup`.
- Your data is in `~/.local/share/lifehub`, with nightly backups in its `backups` folder.
- Background service: `systemctl --user status lifehub` (logs: `journalctl --user -u lifehub`).

## Updating

Install the new `.deb` the same way (it replaces the old one), or run the new `install.sh`. Your
data is kept.

## Uninstalling

- `.deb`: `sudo apt remove lifehub`
- `.tar.gz`: run `./uninstall.sh` from the folder you unpacked (it asks before deleting your data).
