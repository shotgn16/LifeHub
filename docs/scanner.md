# Barcode scanner

Scan groceries as you put them away, as you use them, or when a packet is empty.

<img src="images/scanner.png" alt="Scanner" width="300">

## Modes

| Mode | What a scan does |
|---|---|
| **Stock in** | Adds one item to the kitchen, using the product's usual location and shelf life. |
| **Use up** | Uses one item, taking the soonest use-by date first. |
| **To buy** | Adds the product (or the staple it belongs to) to the shopping list. |

Every scan shows a toast with **Undo**.

## Ways to scan

- **Live camera.** Open **Scan** from the round button in the phone menu. This needs HTTPS (see
  below). Android Chrome uses the phone's built-in barcode reader; iPhones and other browsers use
  the bundled ZXing library. Nothing is downloaded from the internet.
- **Take photo.** Reads the barcode from a picture. This works over plain HTTP too.
- **Type or USB/Bluetooth scanner.** Type the number into the box, or use a scanner that acts as
  a keyboard.
- **Photo of the whole shop.** No barcodes needed: see [Add from photo](kitchen-and-meals.md#add-from-photo).
- **API.** `POST /api/v1/scan` lets a Raspberry Pi or ESPHome scanner by the bin do it hands-free.
  See [api.md](api.md).

## The camera needs HTTPS

Phones only allow camera access on secure pages. Any of these work:

- the Home Assistant app or sidebar, if your HA is reached over HTTPS (Nabu Casa or your own
  certificate)
- your own hostname through Cloudflare Tunnel or a reverse proxy (see
  [remote-access.md](remote-access.md))
- your Plesk domain with Let's Encrypt

Over `http://192.168.x.x:8099`, use **Take photo** instead.

If you refused camera permission, go to the phone's settings → browser or HA app → **Camera** →
**Allow**, then reload the page.

## Learning products

- **First scan.** LifeHub looks the barcode up on [Open Food Facts](https://world.openfoodfacts.org)
  and asks you to check:
  - the name
  - where it's kept
  - how long it keeps
  - which **staple** it counts as (for example, any brand of milk counts as "Milk")
- **After that,** scans of the same product are instant. You can turn off **Instant for known
  items** if you'd rather check every time.
- **Fix mistakes** under **Kitchen → Barcodes**.
- **Privacy.** Only the barcode number is sent to Open Food Facts. You can turn online lookup off
  in **Settings → General**.
