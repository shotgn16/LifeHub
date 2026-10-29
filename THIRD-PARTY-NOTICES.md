# Third-party notices

LifeHub includes or depends on the following third-party components.

| Component | Use | Licence |
|---|---|---|
| [ASP.NET Core / .NET 8](https://github.com/dotnet/aspnetcore) | Runtime and web framework | MIT |
| [MailKit](https://github.com/jstedfast/MailKit) / [MimeKit](https://github.com/jstedfast/MimeKit) | IMAP and MIME parsing (NuGet package) | MIT |
| [PdfPig](https://github.com/UglyToad/PdfPig) | Reading PDF study notes (NuGet package) | Apache-2.0 |
| [ZXing-js](https://github.com/zxing-js/library) 0.21.3 | Barcode decoding in the browser. Vendored as `wwwroot/js/zxing.min.js`; the licence is in `wwwroot/js/zxing.LICENSE.txt`. | Apache-2.0 |

## Services and data

- **[YouVersion Platform](https://platform.youversion.com)**: the verse of the day and passage text
  are fetched with your own app key, subject to YouVersion's terms and each Bible version's
  licence.
- **[StudyLight.org](https://www.studylight.org)** and **[Biblia Graphia](https://bibliagraphia.com)**
  pages are fetched only when you ask for study insights. They're summarised by your local AI for
  your personal use and always linked; nothing is stored or republished.

- **[Open Food Facts](https://world.openfoodfacts.org)** product data is looked up by barcode at
  run time and isn't redistributed with LifeHub. The data is available under the
  [Open Database Licence](https://opendatacommons.org/licenses/odbl/1-0/), and the content under
  [CC BY-SA](https://creativecommons.org/licenses/by-sa/3.0/). Online lookup can be turned off in
  Settings → General.
- **[Ollama](https://ollama.com)**, **Home Assistant**, **Microsoft Graph** and **Exchange Web
  Services** are external services that LifeHub talks to over their public APIs. None of their code
  is included.

Icons in the UI are inline SVG paths drawn for LifeHub.

## OpenStreetMap, Nominatim and Overpass

The optional Deals feature looks up nearby stores using OpenStreetMap data (© OpenStreetMap
contributors, available under the [Open Database Licence](https://www.openstreetmap.org/copyright)),
through the Nominatim and Overpass APIs. Please follow their usage policies; LifeHub only looks
up stores when you change your area (or when none were found last time).

## postcodes.io

UK postcodes are located with [postcodes.io](https://postcodes.io) (free, open data: contains OS
data © Crown copyright and database right, Royal Mail data © Royal Mail copyright and database
right, National Statistics data © Crown copyright and database right).
