# User guide screenshots

These scripts take the screenshots in `../user-guide/images` for the [user guide](../user-guide/README.md).
They use the demo data of `--seed`, a fixed 1280×800 viewport and the light theme.

## Take the screenshots again

1. From the repo root, start the app with demo data and Mailpit: `scripts/preview.sh`.
2. In this folder, install the dependency and take the screenshots:

   ```sh
   npm install
   node login.mjs
   node pages.mjs
   ```

3. Stop `scripts/preview.sh` with Ctrl+C. It removes its containers.

`login.mjs` reads the login codes from Mailpit. It asks for one code for each demo user.
The app allows 3 codes for each email in 15 minutes.
`pages.mjs` takes one screenshot when you give its name, for example `node pages.mjs projects`.
Set `CHROMIUM` when Chromium is not at `/usr/bin/chromium`, and `MAILPIT` when Mailpit is not at `http://localhost:8025`.
`consent.png`, `account-connections.png`, `settings-connected-apps.png` and `settings-connections.png` need a Connected app and a Connection. The scripts do not take them.
