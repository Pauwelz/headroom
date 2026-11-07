# User guide screenshots

These scripts take the screenshots in `../user-guide/images` for the [user guide](../user-guide/README.md).
They use the demo data of `--seed`, a fixed 1280×800 viewport and the light theme.

## Take the screenshots again

1. Start an empty PostgreSQL on port 5432 with the password from `src/Headroom/appsettings.Development.json`:

   ```sh
   DOCKER_HOST=unix:///var/run/docker.sock docker run -d --name headroom-shots-sql \
     -e 'POSTGRES_PASSWORD=Dev_Passw0rd!' -p 5432:5432 postgres:17
   ```

2. From the repo root, run the app with demo data and keep its log:

   ```sh
   ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5002 \
     dotnet run --project src/Headroom --no-launch-profile -- --seed > /tmp/headroom.log 2>&1 &
   ```

3. In this folder, install the dependency and take the screenshots:

   ```sh
   npm install
   node login.mjs /tmp/headroom.log
   node pages.mjs
   ```

4. Stop the app and remove the container: `DOCKER_HOST=unix:///var/run/docker.sock docker rm -f headroom-shots-sql`.

`login.mjs` reads the login codes from the app log. It asks for one code for each demo user.
The app allows 3 codes for each email in 15 minutes.
`pages.mjs` takes one screenshot when you give its name, for example `node pages.mjs projects`.
Set `CHROMIUM` when Chromium is not at `/usr/bin/chromium`.
