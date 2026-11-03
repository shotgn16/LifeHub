# Make it yours

## Look and feel (everyone)

**Settings → Appearance** — each person chooses their own:

- **Theme**: Automatic (follows your computer or phone), Light or Dark.
- **Accent colour**: the colour of buttons, links and highlights.
- **Density**: Comfortable, or Compact to fit more on screen.

## The home screen (everyone)

On **Today**, press **Customise**: tick the cards you want, and use the arrows to put them in
order. Each person has their own layout.

## Sections (admins)

**Settings → Modules** turns whole sections on or off for the household — for example, switch off
**Kitchen**, **Scan**, **Shopping** and **Meals** if you only want tasks and renewals. They
disappear from the menu, the home screen and the bottom bar. Nothing is deleted: switch a module
back on and everything is still there.

## Your own styles (admins)

LifeHub's look is built from a small set of **design tokens** (colours, corners, spacing) in
[`wwwroot/css/theme.css`](../../lifehub/src/LifeHub/wwwroot/css/theme.css). Override any of them
without touching the code:

- **Settings → Appearance → Custom CSS** (admins), or
- a file called **`custom.css`** in LifeHub's data folder.

For example:

```css
:root {
  --accent: #7c3aed;          /* purple buttons and links */
  --radius: 4px;              /* squarer corners */
  --font: "Atkinson Hyperlegible", system-ui, sans-serif;
}
:root[data-theme="dark"] { --bg: #000; }   /* pitch-black dark mode */
.card.widget { box-shadow: none; }         /* flatter home-screen cards */
```

The full list of tokens is at the top of `theme.css`.

## Going further (developers)

Every section is a **module**: one small registration says its name, icon, page, home-screen card
and badge. Adding your own section, or a new home-screen card, is described in
[extending.md](../extending.md).
