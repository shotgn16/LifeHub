# Third-party notices

LifeHub includes or depends on the following third-party components.

| Component | Use | Licence |
|---|---|---|
| [ASP.NET Core / .NET 8](https://github.com/dotnet/aspnetcore) | Runtime and web framework | MIT |
| [MailKit](https://github.com/jstedfast/MailKit) / [MimeKit](https://github.com/jstedfast/MimeKit) | IMAP and MIME parsing (NuGet package) | MIT |
| [ZXing-js](https://github.com/zxing-js/library) 0.21.3 | Barcode decoding in the browser. Vendored as `wwwroot/js/zxing.min.js`; the licence is in `wwwroot/js/zxing.LICENSE.txt`. | Apache-2.0 |

## Services and data

- **[Open Food Facts](https://world.openfoodfacts.org)** product data is looked up by barcode at
  run time and isn't redistributed with LifeHub. The data is available under the
  [Open Database Licence](https://opendatacommons.org/licenses/odbl/1-0/), and the content under
  [CC BY-SA](https://creativecommons.org/licenses/by-sa/3.0/). Online lookup can be turned off in
  Settings → General.
- **[Ollama](https://ollama.com)**, **Home Assistant**, **Microsoft Graph** and **Exchange Web
  Services** are external services that LifeHub talks to over their public APIs. None of their code
  is included.

Icons in the UI are inline SVG paths drawn for LifeHub.
