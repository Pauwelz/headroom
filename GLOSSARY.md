# Headroom

A resource allocation tool for small and medium companies. Planners schedule People on Projects over time. Each company runs its own Instance.

## Language

**Instance**:
One installation of the tool with its own data, used by one company.
_Avoid_: Tenant, workspace

**Operator**:
Whoever installs, upgrades and restores an Instance in the company's own cloud subscription. It can be the company's IT, a partner or us. An Operator is not a Planner and does not need to be a Person in the Instance. The Operator sets what the Instance needs to start, such as the database and the public URL. Planners set everything else in Settings.
_Avoid_: Admin, installer, host

**Test Instance**:
An Instance that runs a Test build or a release candidate before it goes to production. It holds only made-up data and is never used by the company for planning.
_Avoid_: Staging, acceptance, preview

**Test build**:
The image of one open pull request, built from the merge of the PR and `main` and checked on the Test Instance before the PR is merged. The image of `main` (tag `dev`) is deployed to the Test Instance after each merge. Neither is ever released or goes to production.
_Avoid_: Preview, staging build, integration build

**Person**:
Someone who works for the company, as an employee or a freelancer, and can be scheduled. Only humans are scheduled, never rooms or equipment.
_Avoid_: Resource, employee, user

**Planner**:
A Person who can create and change Allocations, the Time off of every Person, and the Settings of the Instance, such as the email server. Usually a managing partner. Other People view the schedule and manage only their own Time off.
_Avoid_: Resource manager, scheduler, admin

**Client**:
The customer a Project is for.
_Avoid_: Customer, account

**Project**:
A piece of work for a Client that People are allocated to. A Project is Tentative, Confirmed or Archived.
_Avoid_: Engagement, job

**Allocation**:
A Person assigned to a Project for a percentage of their contract hours over a date range. A Planner creates it directly, without a request or approval. For one Person and Project, only one Allocation covers a given day.
_Avoid_: Assignment, booking, reservation

**Over-allocation**:
A day on which the Allocations of a Person add up to more hours than their Capacity. It is allowed and shown, never blocked.
_Avoid_: Overbooking, conflict

**Tentative**:
The status of a Project that is not yet signed. All its Allocations are Tentative too. It is the opposite of Confirmed.
_Avoid_: Pencilled, soft booking

**Capacity**:
The hours a Person can work in a period: contract hours minus Time off, public holidays and Company days off.
_Avoid_: Availability

**Contract**:
The hours a Person works on each weekday, valid from a start date until the next Contract of that Person starts. A Person has one or more Contracts over time.
_Avoid_: Work pattern, working hours, schedule

**Time off**:
A period in which a Person does not work. It has no type: leave, sickness and other absence are all the same Time off. It starts and ends on a morning or an afternoon, so only its first and last day can be a half day. A bridge day that one Person takes is Time off. A Person adds, changes and deletes their own Time off without approval, also for past dates. A Planner can do this for every Person.
_Avoid_: Absence, leave (as a general term)

**Company day off**:
A date on which no Person in the Instance works, set by a Planner. Examples: a replacement day for a public holiday, a bridge day for the whole company, a closure, a mandatory training day. It reduces Capacity like a public holiday.
_Avoid_: Holiday (for these days), closure day

**Time off calendar**:
A read-only page that shows who is off when: the Time off of every Person, public holidays and Company days off. Every Person can see it. Time off is changed in the Schedule, not here.
_Avoid_: Leave calendar, absence overview, calendar (alone)

**Calendar feed**:
A URL that a Person adds to their own calendar app to see Time off and days off there. Not built yet.
_Avoid_: iCal link, subscription, Time off calendar

**Archived**:
The status of a Project whose work is finished. Its Allocations stay but are read-only: a Planner cannot add, change or delete them. Unarchive sets the Project to Confirmed.
_Avoid_: Closed, inactive

**Ended**:
A Project whose last Allocation is before today. It is a filter on the Projects page, not a status. An ended Project can still be Tentative or Confirmed.
_Avoid_: Finished, done (for this filter)

**Delete a Person**:
The step that removes a Person with their Contracts, Allocations, Time off, Passkeys, Access tokens and Connections. Past Utilisation and Project hours drop. A Planner can do it at any time, and it runs on its own 12 months after the end date. It cannot be undone.
_Avoid_: Erase, anonymise, archive (for People)

**Utilisation**:
Allocated hours divided by Capacity for a Person or group in a period.
_Avoid_: Occupancy, load

**Login code**:
The 6-digit code that Headroom sends by email to a Person so they can log in. It works for a short time and only once. The first Planner gets one during setup, unless they set up Company login there.
_Avoid_: OTP, password, magic link

**Passkey**:
A key on a device or in a password manager that lets a Person log in without a Login code. A Person can have several. Each one works only for the Instance where they saved it.
_Avoid_: WebAuthn credential, security key, FIDO key, password

**Event**:
A message that Headroom sends when an Allocation, Time off or Project status changes. It carries the full record. Headroom sends it to every Webhook, and the receiver decides what to do with it.
_Avoid_: Notification, hook

**Webhook**:
A URL that a Planner adds in Settings. Headroom sends each Event to it.
_Avoid_: Subscription, endpoint, integration

**Company login**:
The login of the company's own identity provider, such as Microsoft Entra ID, that a Planner adds in Settings. An Instance has at most one. It works only for a Person who is already in the Instance, and a session from it lasts 12 hours. A Planner can make it the only way to log in for People with a company email domain. Those People then cannot use a Login code, and their Passkeys are deleted. It is optional: an Instance can run with Login codes only.
_Avoid_: SSO, OAuth provider, external login, IdP

**Access token**:
A secret that belongs to one Person, so that an AI assistant or a script can call Headroom for that Person. It has the rights of its Person, and no more. It is read only unless its Person allows it to change things. It can have an expiry date.
_Avoid_: API key, password

**Connected app**:
An application, such as Claude or Copilot Studio, that a Planner adds in Settings so that each Person can connect it to Headroom with their own login. It acts only through a Connection.
_Avoid_: OAuth client, application, integration

**Connection**:
The permission that one Person gives one Connected app to act for them. It has the rights of its Person, and no more. It is read only unless its Person allows it to change things. The Person or a Planner can revoke it.
_Avoid_: Grant, authorization, session
