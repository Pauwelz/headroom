# Settings page (Planners only)

![The Settings page with the nav on the left, and the Company, Instance and Access and integrations groups on the right](images/settings.png)

The page has three groups: **Company**, **Instance** and **Access and integrations**.
- The nav on the left lists each area. Click an area to jump to it. On a phone, the nav shows above the page.
- A number in the nav shows that an area needs action:
  at **Access tokens**, the number of tokens that expire within 7 days, and at **Webhooks**, the number of failed Events.
- Under each heading, a line sums up the area, for example "3 tokens of 2 People" or the names of the Webhooks.
- **Company name** and **Connected apps** start collapsed. Click the arrow to show them.
  Connected apps stays open while it shows a new secret, an error or an Edit row.

- **Company name.** The first-run screen sets it. Enter a new name and click **Save**.
  The login page, the app bar and the sender of the login email show the new name.
- **Company days off.** A Company day off is a day on which nobody works.
  It reduces Capacity like a public holiday.
  Use the arrows to change the year.
  - Belgian public holidays show in grey. You cannot change them.
    A public holiday on a Saturday or Sunday shows the note "On a Saturday: add a replacement day?" (or Sunday).
    To add the replacement day, use the row at the bottom.
  - Each Company day off has **Edit** and **Delete**.
  - The row at the bottom adds a date and a name, for example a bridge day.
    **Add** stays disabled until the date and the name are filled in.
  - Each date can have one Company day off.
    A note warns if the date is a public holiday (no effect) or a weekend.
- **Email server.** This section sets the server that sends the Login codes.
  - Fill in **Host**, **Port**, **User name**, **Password** and **From address**.
  - Click **Save**. Headroom sends a test email to you first. It saves the settings only if the test succeeds.
    If it fails, the page shows "Could not connect to the email server." and the old settings stay.
  - Headroom never shows the password again. The field says "Stored. Leave empty to keep it." Leave it empty to keep the password.
    If you change the host, the port or the user name, enter the password again.
  - The line under the heading shows the host, "The Operator's server" when the Operator set the server in the environment, or "Not set".
- **Company login.** This section lets People log in with the login of your company. An Instance has at most one Company login.
  - Choose the **Kind**: **Microsoft Entra ID** or **Other OpenID Connect**. Fill in **Issuer URL**, **Client ID** and **Client secret**.
  - Register the **Redirect URI** at the provider. Select the copy button to copy it. It is the address of your Instance followed by `/signin-oidc`.
  - Select **Test and save**. The browser goes to the provider and you log in. Headroom saves the settings only if the test login works **and** is your own login.
    If the test fails, the page shows why: the provider refused the login, the provider gave no verified email, or the login belongs to another Person.
    The old settings stay.
  - Headroom matches a login with a Person who is already in Headroom. It never creates a Person from the provider.
    Enter the email that the provider returns as the Person's email. For Microsoft Entra ID, this is the user principal name (UPN).
    The first login links the provider identity to the Person. Later logins use this link, even when the email changes.
    A second identity with the same email cannot take over the Person.
  - A Company login session lasts 12 hours and does not extend. A person who is disabled at the provider can keep an open session for up to 12 hours.
    Set the end date of the Person to stop access at once.
  - Headroom never shows the client secret again. The field says "Stored. Leave empty to keep it." If you change the kind, the issuer or the client ID, enter the secret again.
  - **Remove** deletes the Company login and the links of all People. To change to another provider, remove the Company login first.
  - **Microsoft Entra ID:** register Headroom in the Microsoft Entra admin center.
    1. Open **App registrations** and select **New registration**. Choose **Accounts in this organizational directory only** (single tenant).
    2. Under **Redirect URI**, choose the type **Web** and enter the redirect URI from Headroom.
    3. Open **Certificates & secrets**, select **New client secret** and copy the value.
    4. Copy the **Application (client) ID** and the **Directory (tenant) ID** from the overview page.
    5. In Headroom, choose **Microsoft Entra ID**. Enter `https://login.microsoftonline.com/{tenant id}/v2.0` as the issuer, the client ID and the secret.
    Headroom refuses `common`, `organizations` and `consumers` as the tenant, because they allow accounts of other companies.
  - **Local test with the mock provider:** `scripts/preview.sh` also starts a mock provider.
    Choose **Other OpenID Connect**, enter `http://localhost:8080/default` as the issuer, and any client ID and secret.
    At its login form, enter any user name and the claims `{"email":"planner@example.test","email_verified":true}`.
- **Telemetry.** This section sets which personal data Headroom adds to the telemetry of the Operator.
  - **Send Person id** lets the Operator count active users and trace a fault of one Person.
  - **Send client IP address** shows the real client address instead of the proxy.
  - Click **Save**. The change applies to the next request.
  - If the page says "Headroom sends no telemetry: the Operator has not set an exporter.", the switches have no effect yet. You can still set them.
- **Access tokens.** This list shows the Access tokens of all People, with the owner of each token.
  It shows the expiry date and the **Can change** state of each token. It never shows a secret. **Revoke** stops one token at once. The other tokens keep working.
  Each Person creates their own tokens on the [Account page](account.md).

- **Connected apps.** A Connected app is an app, such as Claude or Copilot Studio, that each Person can connect to with their own login.
  - The row at the bottom adds a Connected app with a name and one or more redirect URLs. Enter one URL per line.
    Each redirect URL must be an absolute https URL without a fragment.
    **Add** stays disabled until the name and the redirect URLs are valid.
  - Headroom shows the client ID and the client secret. It shows the secret once. Copy it then.
  - **Edit** changes the name and the redirect URLs. The client ID and the client secret stay the same, and the Connections keep working.
  - **New secret** replaces the old secret at once. The old secret no longer works. Headroom shows the new secret once.
  - **Delete** removes the Connected app and all its Connections.
  ![The Connected apps section after adding Claude, with the client ID and the client secret](images/settings-connected-apps.png)
- **Connections.** This list shows the Connections of all People: the Connected app, the Person, the date created, **Last used** and **Can change**.
  **Revoke** stops one Connection at once. The other Connections keep working.
  ![The Connections list in Settings with the Person of each Connection](images/settings-connections.png)
  Each Person sees and revokes their own Connections on the [Account page](account.md).

## Webhooks

A Webhook is a URL that receives an Event when an Allocation, Time off or Project status changes.
For example, a Power Automate flow can post to Teams.

- The row at the bottom adds a Webhook with a name and a URL.
  Headroom shows the secret (`whsec_...`) once. Copy it then.
- The URL must use https and must point to a public address, not to an internal address.
  A development instance also accepts http.
  **Add** stays disabled until the name is filled in and the URL is valid.
  The server checks the URL again.
- Each Webhook has **Rename**, **New secret** and **Delete**. **Delete** also deletes the unsent Events of the Webhook.
  **New secret** replaces the old secret at once. Headroom shows the new secret once.
- Headroom sends every Allocation and Time off create, change and delete to every Webhook.
  Headroom also sends an Event when a Project status changes. Other Project edits send no Event.
  The Event is CloudEvents 1.0 JSON.
  Its `type` is `headroom.<Record>.created`, `.changed` or `.deleted`, for example `headroom.Allocation.created`.
  The Record is `Allocation` or `TimeOff`. A Project status change has the type `headroom.Project.changed`.
  The `data` is the full record, with the name and email of the Person.
  When you delete a Project, Headroom sends a `headroom.Allocation.deleted` Event for each of its Allocations.
  A delete Event carries the last state.
- Headroom signs each Event with the Standard Webhooks headers
  `webhook-id`, `webhook-timestamp` and `webhook-signature`.
- If a send fails, Headroom retries it with a growing delay while the app runs.
  After 24 hours Headroom stops retrying.
  Settings lists the newest 50 failed Events per Webhook, with the last error.
- An Event can arrive more than once. Receivers must use `webhook-id` to ignore repeats.
- Events that Headroom sent before **Erase personal details** keep the name at the receiver.
  Headroom cannot recall them.
