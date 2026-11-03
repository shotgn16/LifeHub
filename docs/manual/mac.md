# Installing LifeHub on a Mac

Works on **macOS 12 Monterey or later**.

## Which download?

- **Apple chip** (M1, M2, M3, M4 or later — most Macs from 2021 on): `LifeHub-<version>-macOS-AppleSilicon.dmg`
- **Intel chip**: `LifeHub-<version>-macOS-Intel.dmg`

Not sure? **Apple menu → About This Mac**: it says *Chip: Apple M…* or *Processor: Intel*.

## Install

1. Download the right `.dmg` from the
   [Releases page](https://github.com/shotgn16/LifeHub/releases/latest) and double-click it.
2. Drag **LifeHub** onto the **Applications** folder in the window that opens.
3. Open **Applications** and double-click **LifeHub**.
4. **"LifeHub can't be opened"** or **"Apple could not verify…"**? LifeHub isn't notarised by Apple
   yet, so the first time you have to allow it:
   1. Press **Done** (not *Move to Bin*).
   2. Open **System Settings → Privacy & Security**, scroll down to *"LifeHub was blocked…"* and
      press **Open Anyway**. Enter your password if asked, then press **Open**.
   - Or, in Terminal: `xattr -dr com.apple.quarantine /Applications/LifeHub.app`

You only need to do this once.

## Set it up

LifeHub opens with its **setup assistant** — the steps are the same on every computer:

1. **How do you want to use LifeHub?** This Mac only · this Mac + my phone · a household server ·
   Docker · Home Assistant · LifeHub somewhere else.
   - For **a household server**, *When the computer starts* asks for your Mac's password once.
     Also turn off sleep: **System Settings → Energy** (or **Battery → Options**) → *Prevent
     automatic sleeping when the display is off*.
2. **Allow incoming connections?** For the phone and server options, macOS may ask about
   LifeHub and your local network. Press **Allow**.
3. **Create your account.**
4. **The AI assistant**: press **Yes, install Ollama** to have LifeHub install it into
   Applications and download a model that fits your Mac, or **No thanks** to do it later
   ([The AI assistant](ai.md)).
5. **Connect your phone**: the address and API key for the [phone app](phone-apps.md).
6. **Open LifeHub.**

## Every day

- Open LifeHub from **Applications**, Launchpad or Spotlight. Keep it in the Dock: right-click its
  icon → **Options → Keep in Dock**.
- To run the setup assistant again: **Settings → This computer → Change how LifeHub runs…**
- Your data is in `~/Library/Application Support/LifeHub` (in Finder: **Go → Go to Folder…**).
  Backups are made every night in its `backups` folder.

## Updating

Download the new `.dmg`, drag LifeHub to Applications again and choose **Replace**. Your data is
kept.

## Uninstalling

1. If you chose the phone or server option: **Settings → This computer → Change how LifeHub
   runs… → This computer only** (this removes the background service).
2. Drag **LifeHub** from Applications to the Bin.
3. To delete your data too, delete `~/Library/Application Support/LifeHub`.
