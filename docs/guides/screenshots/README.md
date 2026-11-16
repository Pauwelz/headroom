# User guide screenshots

These scripts take the screenshots in `../user-guide/images` for the [user guide](../user-guide/README.md).
They use the demo data of `--seed`, a fixed 1280×800 viewport and the light theme.

## Take the screenshots again

1. From the repo root, start the app with demo data: `scripts/preview.sh`.
2. In this folder, install the dependency and take the screenshots:

   ```sh
   npm install
   node login.mjs
   node pages.mjs
   ```

3. Stop `scripts/preview.sh` with Ctrl+C. It removes its containers.

`login.mjs` logs in at the mock provider, once for each demo user.
`pages.mjs` takes one screenshot when you give its name, for example `node pages.mjs projects`.
Set `CHROMIUM` when Chromium is not at `/usr/bin/chromium`.
`account-connections.png`, `settings-connected-apps.png` and `settings-connections.png` need a Connected app and a Connection. The scripts do not take them.
