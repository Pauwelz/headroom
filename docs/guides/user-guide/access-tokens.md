# Access tokens page

An Access token lets a script or an AI assistant call Headroom for you.
The token belongs to you and has your rights, and no more.
A Person who is not a Planner reads what they can read in the app.
A Planner's token also reads the People list.
A token is read only by default. A read-only token cannot change anything.
A Planner can allow a token to change things with **Can change**.

- Enter a name, choose when the token expires, and select **Create**. Headroom shows the token once. Copy it at once.
  Headroom keeps only a hash, so it cannot show the token again.
  **Create** stays disabled until you enter a name.
- **Can change** is off by default. Only a Planner sees it. With it, the token creates, changes and deletes data through the REST API and the MCP write tools, with the rights of the Planner.
  Headroom records each change for that Planner. The token loses this right when the Planner loses the Planner role.
  A read-only token gets the answer 403 on every change.
- The choices are 30 days, 90 days, 1 year and Never. The default is 90 days. You cannot change the choice later.
- The list shows the expiry date of each token. A token that expires within 7 days has the mark "Expires soon".
- An expired token stops working and gets the answer 401. It stays in the list and shows "Expired".
  Revoke it to remove it. Headroom does not renew or delete tokens by itself. To get a new token, create one.
- Send the token in the header `Authorization: Bearer <token>`.
- **Revoke** stops one token at once. The other tokens keep working.
- You see only your own tokens on this page. A Planner sees all tokens on the [Settings page](settings.md).
- A token stops working when its Person has left, or when a Planner erases the personal details of the Person.
- A token cannot create, list or revoke Access tokens. Use the page for that.

## Connections

A Connection lets a Connected app, such as Claude, act as you.
You make a Connection when you allow the app on its consent screen. The Connection has your rights, and no more.
It is not an Access token. Use an Access token only for a client that cannot use a Connected app.

![The Connections list on the Access tokens page with one Connection to Claude](images/access-tokens-connections.png)

- The list shows each Connection with the Connected app, **Can change**, the date it was created and **Last used**.
  **Last used** is the date of the newest token of the Connection. A Connection that no one used yet shows "Never".
- **Can change** shows whether you allowed the app to change the planning. Only a Planner can allow it.
- **Revoke** stops one Connection at once. The app must ask you to connect again. Your other Connections keep working.
- You see only your own Connections on this page. A Planner sees all Connections on the [Settings page](settings.md).
- A Connection stops working when its Person has left, or when a Planner erases the personal details of the Person.
