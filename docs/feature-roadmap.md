# Feature Roadmap

**Created**: 2026-08-31 | **Status**: Living document

The master inventory of planned features. `specs/` holds only what has entered the Spec Kit
lifecycle; this file holds everything, so that a capability nobody has started is still written
down somewhere.

Numbering here is a plan, not a reservation. No `specs/00x-*` directory exists until its feature is
specified, and the numbers below may shift if priorities change - though once a feature enters
`specs/` its number is fixed.

---

## Where the scope comes from

This roadmap is derived from four sources in this repository, not invented:

| Source | What it establishes |
|--------|--------------------|
| `specs/001-project-foundation/spec.md`, Out of Scope | The clearest inventory statement in the repo: it names authentication, users/roles/permissions, departments/branches/teams, customers, tickets, SLA and escalation, agent dashboard, notifications, knowledge base, customer portal, email/WhatsApp/SMS/live chat channels, reporting, AI assistance, audit browsing, and external integrations as future features |
| `.specify/memory/constitution.md` | Principle XV names the required adapters - WhatsApp, SMS, email, ERP, AI providers, external customer systems. Principle XIV establishes AI as optional. Principle XII requires attachment abstraction with malware/security rules. Principle IX requires customer and ticket history. The permission examples name `customers.*`, `tickets.assign`, `tickets.escalate`, `users.manage`, `reports.view` |
| `specs/002-*` and `specs/003-*`, Out of Scope | Deferrals with named owners: portal identity, role definition, placement, and organizational scope enforcement |
| Stakeholder capability list (2026-08-31) | The requested inventory, restated in the Scope Coverage Matrix below |

Where the stakeholder list names something the repository never mentions, it is marked in the
coverage matrix. Those items are still committed scope - the stakeholder is the source of scope -
but the distinction matters when reading a requirement back later.

---

## Scope-gap analysis

Six capabilities are required by something in this repository and appear in **neither** the existing
specs nor the stakeholder capability list. They are the reason this document is longer than a
renumbering exercise.

| # | Gap | Where the requirement comes from | Where it now lives |
|---|-----|----------------------------------|--------------------|
| 1 | **Role and permission definition** - creating roles, editing which permissions they grant | Deferred explicitly out of feature 004 during its design session, on the grounds that defining authority is a different act from granting it | **022** |
| 2 | **Background processing and real-time transport** | `001` defers "real-time transport and background job processing setup". SLA timers, notifications, and live chat cannot work without them | **009** |
| 3 | **Attachment storage implementation and malware scanning** | `001` defers "attachment storage implementation"; Constitution XII requires the abstraction plus defined file types, sizes, authorization and malware rules | **008** |
| 4 | **Organizational scope enforcement on data** | `003` states plainly: "The feature that introduces scoped data introduces its enforcement." No query is scoped today | **005**, then every feature after it |
| 5 | **Customer portal identity** - CRM-owned customer accounts, password storage, reset, self-registration, activation | `002` defers all of it by name. The stakeholder list says "customer portal" without the identity substrate underneath it | **014** |
| 6 | **Audit browsing and retention** | `001` delivers "a writing surface only; no audit browsing or retention behavior" | **023** |

Two further observations, neither a gap:

- **Rate limiting** was deferred by `001` to the authentication feature and delivered there. Closed.
- A **`clamav` container** is running in the local Docker environment but is referenced nowhere in
  the repository. It is consistent with the malware requirement in Constitution XII, but it is
  environment evidence rather than scope evidence, so gap 3 rests on the constitution instead.

---

## Feature inventory

| ID | Feature | Status | Depends On | Scope |
|----|---------|--------|------------|-------|
| 001 | Project Foundation | Complete | - | Monorepo, four-layer backend, Angular workspace, EF Core migrations, API conventions, error contract, i18n/RTL, logging, health, audit write surface, test and lint gates |
| 002 | Authentication & Session Management | Complete | 001 | OIDC handshake, CRM-issued credentials, server-side sessions with rotating renewal, role-to-permission store, rate limiting |
| 003 | Organization Structure | Complete | 001, 002 | Departments, branches, teams; containment rules; team move with member resync; retirement of provider-asserted placement |
| 004 | Identity Administration | In Progress | 002, 003 | People administration and placement; pre-provisioning by email; verified-email claim rule; roles granted as a union; lockout guards |
| 005 | Customer Management | Planned | 004 | Customer records, profiles, contact information, search and filtering, activation; **first enforcement of organizational scoping** |
| 006 | Ticket Core | Planned | 005 | Ticket creation and editing, categories, priorities, status workflow, status history |
| 007 | Ticket Assignment & Agent Workspace | Planned | 006 | Assignment to agent/team, queues, the agent's working view and personal dashboard |
| 008 | Notes, Attachments & Interaction Timeline | Planned | 006 | Internal notes, customer-visible notes, attachment storage abstraction with type/size/authorization/malware rules, unified customer and ticket timeline |
| 009 | Platform Services | Planned | 001 | Background job processing, scheduling, and real-time push. No user-facing surface; exists because 010, 011 and 018 each need it and must not each invent it |
| 010 | SLA & Escalation | Planned | 007, 009 | SLA policies and targets, response and resolution timers, breach detection, escalation rules and escalation history |
| 011 | Notifications, Tasks & Reminders | Planned | 009, 007 | In-app notification centre, per-user tasks and reminders, notification preferences |
| 012 | Quick Replies & Templates | Planned | 006 | Canned responses and message templates, bilingual, reusable by agents and later by channels |
| 013 | Knowledge Base | Planned | 004 | Articles, FAQs and solutions; categories; authoring and publishing workflow; search |
| 014 | Customer Portal & Web Form Intake | Planned | 006, 013 | Portal identity for the customer population (accounts, reset, self-registration, activation), ticket submission and tracking, knowledge base access, anonymous web-form intake |
| 015 | Customer Feedback & Satisfaction | Planned | 006, 014 | Satisfaction surveys on resolution, CSAT capture and storage, feedback visible to agents |
| 016 | Channel Framework & Email Channel | Planned | 006, 009 | The inbound/outbound channel abstraction, message threading onto tickets, identity matching, and email as its first implementation |
| 017 | WhatsApp & SMS Channels | Planned | 016 | WhatsApp and SMS adapters on the channel framework, with the retry and idempotency rules Constitution XV requires |
| 018 | Live Chat | Planned | 016, 009 | Real-time agent/customer chat, chat-to-ticket conversion, availability and routing |
| 019 | Reporting & Dashboards | Planned | 007, 010, 015 | Operational dashboards and reports over tickets, SLA compliance, agent performance and satisfaction; export |
| 020 | AI Assistance | Planned | 006, 008, 013 | The AI provider abstraction plus conversation summaries, suggested replies, suggested categorization, and suggested solutions - all labelled and user-accepted |
| 021 | AI Chatbot & Deflection | Deferred | 020, 013, 014 | Customer-facing assistant answering from the knowledge base with handoff to a human. Deferred: Constitution XIV makes AI optional, and this depends on a mature knowledge base |
| 022 | Role & Permission Administration | Planned | 004 | Creating and editing roles, editing the permission matrix, with the self-lockout protections feature 004 established |
| 023 | Audit Log Browsing & Retention | Planned | 004 | Browsing, searching and filtering the audit trail 001 has been writing since the beginning; retention policy |
| 024 | System Configuration & Branding | Planned | 004 | Tenant-level settings, business hours and holidays, branding (logo, colours, sender identity) applied to portal and outbound messages |
| 025 | External Integrations & ERP | Planned | 016 | Outbound public API and webhooks; ERP integration behind an adapter. The ERP portion is **conditional** - see "could not be mapped confidently" below |

**25 features**: 3 Complete, 1 In Progress, 20 Planned, 1 Deferred.

---

## Milestones

### Platform Foundation — 001 to 004
Everything a business feature stands on: the codebase, who you are, where you sit, and who may do
what. Nothing here is visible to a customer. **Status: three of four complete.**

### Core CRM MVP — 005 to 008
The smallest product that does the job the CRM exists for: record a customer, raise a ticket, route
it to somebody, and work it with notes and attachments against a visible history. A support team
could use this. **This is the MVP cutoff.**

### Operational Support CRM — 009 to 012
What turns a usable ticket system into a managed one: promises with timers behind them, work that
chases the agent rather than waiting to be found, and replies that do not get retyped.

### Channels & Self-Service — 013 to 018
Where the customer arrives from. Knowledge base first because the portal and the chatbot both read
it; then the portal and web form; then satisfaction; then the channel framework and the three
channels on top of it.

### Reporting & Intelligence — 019 to 020
Seeing what the operation is doing, and the first AI assistance over it. Both need real data, which
is why they sit after the milestones that produce it.

### Advanced / AI & Integrations — 021 to 025
Capabilities that are valuable, optional, or conditional: the chatbot, role definition, audit
browsing, configuration and branding, and outward integration.

---

## Scope Coverage Matrix

Every capability from the stakeholder list, mapped to its owning feature. The **In repo?** column
records whether the repository independently documents the requirement, or whether it rests on the
stakeholder list alone.

| # | Capability | Owning feature(s) | In repo? |
|---|-----------|-------------------|----------|
| 1 | Project foundation | 001 | Yes - delivered |
| 2 | Authentication / session management | 002 | Yes - delivered |
| 3 | Users / roles / permissions / RBAC | 004 (grant), 022 (define), 002 (enforce) | Yes - 001, 002 |
| 4 | Organization structure: departments, branches, teams | 003 | Yes - delivered |
| 5 | Agents (as placed people) | 004 | Yes - 003 |
| 6 | Customer management | 005 | Yes - 001, constitution |
| 7 | Customer profiles and contact information | 005 | Yes - constitution permissions |
| 8 | Customer interaction/history | 008 | Yes - Constitution IX |
| 9 | Notes and attachments | 008 | Yes - Constitution XII |
| 10 | Ticket management | 006 | Yes - 001, constitution |
| 11 | Ticket categories and priorities | 006 | Stakeholder list |
| 12 | Ticket workflow/status/history | 006 | Yes - Constitution IX |
| 13 | Ticket assignment | 007 | Yes - `tickets.assign` |
| 14 | Escalation | 010 | Yes - `tickets.escalate` |
| 15 | SLA management | 010 | Yes - 001 |
| 16 | Agent workspace/dashboard | 007 | Yes - 001 |
| 17 | Tasks and reminders | 011 | Stakeholder list |
| 18 | Notifications | 011 | Yes - 001 |
| 19 | Team collaboration / internal notes | 008 | Stakeholder list |
| 20 | Quick replies/templates | 012 | Stakeholder list |
| 21 | Knowledge base | 013 | Yes - 001 |
| 22 | FAQs / articles / solutions | 013 | Stakeholder list |
| 23 | Customer portal | 014 | Yes - 001, 002 |
| 24 | Customer feedback/satisfaction | 015 | Stakeholder list |
| 25 | Email channel | 016 | Yes - 001, Constitution XV |
| 26 | WhatsApp channel | 017 | Yes - Constitution XV |
| 27 | SMS channel | 017 | Yes - Constitution XV |
| 28 | Live chat | 018 | Yes - 001 |
| 29 | Web-form ticket intake | 014 | Stakeholder list |
| 30 | Reports | 019 | Yes - `reports.view` |
| 31 | Dashboards | 019 | Yes - 001 |
| 32 | Ticket/SLA/agent/satisfaction analytics | 019 | Stakeholder list |
| 33 | AI summaries | 020 | Yes - Constitution XIV |
| 34 | AI suggested replies | 020 | Yes - Constitution XIV |
| 35 | AI categorization | 020 | Yes - Constitution XIV |
| 36 | AI suggested solutions | 020 | Yes - Constitution XIV |
| 37 | AI / chatbot capabilities | 021 | Yes - Constitution XIV |
| 38 | Audit logging | 001 (write), 023 (browse, retain) | Yes - delivered + deferred |
| 39 | System configuration | 024 | Stakeholder list |
| 40 | Branding | 024 | Stakeholder list |
| 41 | Localization (Arabic/English, RTL/LTR) | **Cross-cutting** - 001 and every feature | Yes - Constitution VII |
| 42 | Responsive / mobile-friendly behavior | **Cross-cutting** - every feature | Stakeholder list |
| 43 | External integrations / APIs | 025 | Yes - Constitution XV |
| 44 | ERP integration where required | 025 (conditional) | Yes - Constitution XV |

Every capability maps. Two are cross-cutting rather than features, and are treated as such below.

---

## Dependency Order

```text
001 ─► 002 ─► 003 ─► 004 ─┬─► 005 ─► 006 ─┬─► 007 ─┬─► 010 ◄── 009
                          │               │        │
                          │               ├─► 008  ├─► 011 ◄── 009
                          │               │        │
                          │               ├─► 012  └─► 019 ◄── 010, 015
                          │               │
                          ├─► 013 ────────┼─► 014 ─► 015
                          │               │
                          ├─► 022         └─► 016 ─┬─► 017
                          │                        └─► 018 ◄── 009
                          └─► 023
                                          020 ◄── 006, 008, 013 ─► 021
                                          024, 025
```

**The critical path** runs 001 → 002 → 003 → 004 → 005 → 006. Nothing meaningful can be built
around it: tickets need customers, customers need people to own them, and people need somewhere to
sit. Every delay on this path delays everything.

**What can be built in parallel**, once its dependency lands:

| Feature | Available after | Why it is independent |
|---------|-----------------|-----------------------|
| 009 Platform Services | 001 | Touches no business domain. Buildable at any time, and worth doing early so 010, 011 and 018 do not each improvise |
| 013 Knowledge Base | 004 | Authored content with no ticket relationship. A second team could build it beside 005/006 |
| 022 Role & Permission Administration | 004 | Placed late by priority, not by dependency. Pull it forward whenever custom roles are wanted |
| 023 Audit Log Browsing | 004 | Reads a trail written since 001. Depends on nothing being built now |
| 024 System Configuration & Branding | 004 | Independent, but branding is most useful *before* 014 ships a customer-facing surface |
| 008 Notes & Attachments | 006 | Parallel with 007; the two touch different parts of a ticket |
| 017, 018 | 016 | Both sit on the channel framework and are independent of each other |

**Sequencing constraints worth stating:**

- **009 before 010, 011 and 018.** Each needs timers or push; without 009 the first one to arrive
  invents infrastructure the others then work around.
- **013 before 014 and 021.** Both read the knowledge base; building either first means designing
  against content that does not exist.
- **016 before 017 and 018.** The framework decides threading and identity matching; retrofitting
  those onto a channel already shipped is the expensive version.
- **005 carries the scoping decision.** It is the first feature with data worth scoping by
  organization, so it sets the pattern every later feature follows. Getting it wrong is expensive
  in a way that is invisible until feature 010.

---

## Deferred / Cross-Cutting Requirements

These are not features and must not be scheduled as though they were. Each is a property every
feature carries.

| Requirement | Nature | How it is satisfied |
|-------------|--------|--------------------|
| **Localization (ar/en, RTL/LTR)** | Cross-cutting, Constitution VII | Every feature ships both languages and logical CSS. Enforced per feature by `i18n:check` and `css:check`, not by a localization feature |
| **Responsive / mobile-friendly behavior** | Cross-cutting | Every screen. There is no "make it responsive" feature; a screen that is not responsive is not done |
| **Backend-enforced authorization** | Cross-cutting, Constitution IV | Every endpoint declares its permission. 002 and 004 build the mechanism; every feature after uses it |
| **Organizational scope enforcement** | Cross-cutting from 005 onward | 003 deferred it to "the feature that introduces scoped data". 005 sets the pattern; every later feature applies it |
| **Audit trail writing** | Cross-cutting, delivered | 001 built the writing surface. Each feature records its own security-sensitive actions. Only *browsing* is a feature (023) |
| **Structured logging and correlation** | Cross-cutting, delivered | 001. Each feature logs its own operations without secrets |
| **Error contract and the six UI states** | Cross-cutting, delivered | 001. Constitution X applies to every screen |
| **Testing** | Cross-cutting, Constitution XIII | Business rules, authorization and validation failures, per feature |
| **Deployment pipelines, environment provisioning** | Infrastructure, out of product scope | Deferred by 001. Operational work, not a Spec Kit feature |

---

## Recommended / Not Original Scope

Not committed. Recorded so the ideas are not lost, and clearly separated so they are never mistaken
for requirements.

| Idea | Why it might matter | Suggested placement |
|------|--------------------|--------------------|
| Ticket merge and duplicate detection | Two tickets for one problem is the most common data-quality failure in a support queue, and merging after the fact is painful | An addition to 006 |
| Bulk actions on tickets | Reassigning a departing agent's queue one ticket at a time is the moment a team asks for this | An addition to 007 |
| Data retention and personal-data erasure | Likely a legal requirement wherever this is deployed; the constitution's soft-delete stance makes erasure a deliberate design question rather than a default | Its own feature, beside 023 |
| Customer-facing SLA visibility | Showing a customer their own response target changes expectations more cheaply than meeting them faster | An addition to 014 |
| Saved views and filters for agents | Falls out of 007 naturally and is usually asked for immediately afterwards | An addition to 007 |
| Directory synchronisation of organization structure | 003 explicitly names this as a possible future feature with its own reconciliation rules | Its own feature, after 022 |

---

## Maintaining this document

Update the status column when a feature enters or leaves the Spec Kit lifecycle. When a feature is
specified, its `specs/NNN-name/` directory is created by `/speckit.specify` and its number becomes
fixed. Numbers not yet in `specs/` may be reordered freely.
