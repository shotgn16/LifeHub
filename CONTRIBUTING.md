# Contributing

Thanks for your interest in LifeHub. Bug reports, ideas and pull requests are welcome.

## Reporting bugs

[Open an issue](https://github.com/Shotgn16/LifeHub/issues/new/choose) with:

- the LifeHub version (shown on the add-on page in HA, or at `/health`)
- how you run it: HA add-on, Docker, Plesk or IIS
- what you did, what you expected, and what happened
- the relevant log lines, **with email addresses, names and tokens removed**

## Development setup

```bash
git clone https://github.com/Shotgn16/LifeHub.git
cd LifeHub/lifehub/src/LifeHub
dotnet run          # http://localhost:5080
```

You need the .NET 8 SDK. See [docs/architecture.md](docs/architecture.md) for how the code is laid
out, and [docs/extending.md](docs/extending.md) for the main extension points.

## Pull requests

- Keep each change focused, and describe *why* as well as *what*.
- Match the existing style: nullable enabled, file-scoped namespaces, and small services injected
  through DI.
- Make sure `dotnet build` passes with no new warnings.
- Update the docs and add a line under **[Unreleased]** in `CHANGELOG.md` for any change users
  will notice.
- Never commit a `data/` folder, tokens or personal data.
