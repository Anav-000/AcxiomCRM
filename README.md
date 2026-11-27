# AcxiomCRM

**AcxiomCRM** is an enterprise-grade, role-based Customer Relationship Management (CRM) web application built using **ASP.NET Core 8.0 MVC**, **Entity Framework Core with SQLite**, **ASP.NET Core Identity**, **Bootstrap 5**, and **Chart.js**.

The application covers the full customer-sales lifecycle from lead generation, qualification, and lead conversion, to opportunity pipeline tracking, customer management, follow-up touchpoint scheduling, multi-type activity logging, system audit trails, and multi-tier reporting with fine-grained server-enforced role-based authorization.

---

## 1. System Requirements & Windows Setup

### Prerequisites
- **Operating System**: Windows 10/11 or Windows Server (also compatible with Linux / macOS via cross-platform .NET 8).
- **Runtime & SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Version `8.0.428` or newer).
- **Database**: SQLite (embedded, zero-configuration local file `AcxiomCRM.db`).
- **Web Browser**: Modern Chromium-based browser (Chrome, Edge) or Firefox.

### PowerShell Setup
Ensure `dotnet` is available in your PowerShell session:
```powershell
# Verify .NET 8 installation
dotnet --version
# Expected output: 8.0.xxx
```

If `dotnet` is installed in your local AppData directory:
```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
```

---

## 2. Quick Start & Run Commands

### 1. Restore & Build (Release Configuration)
```powershell
dotnet restore
dotnet build -c Release
```

### 2. Database Migrations & Initial Setup
Migrations apply automatically on startup via `context.Database.MigrateAsync()`. Alternatively, you can apply them manually:
```powershell
dotnet ef database update
```

### 3. Launch the Application
Run the app locally on port `5234`:
```powershell
$env:ASPNETCORE_URLS = "http://localhost:5234"
dotnet run -c Release --no-build
```
Navigate to: **[http://localhost:5234](http://localhost:5234)**

### 4. Automated Verification Suites
The application includes self-verifying CLI test runners:
```powershell
# Phase 1 Verification (Identity, Password Policy, Lockout, Dashboard Shell)
dotnet run -c Release --no-build -- --verify-phase1

# Phase 2 Verification (Customer/Lead Uniqueness, Ownership Scoping, Lead Conversion)
dotnet run -c Release --no-build -- --verify-phase2

# Phase 3 Verification (Opportunity Business Rules, Follow-Ups, Activities, Lead Protection)
dotnet run -c Release --no-build -- --verify-phase3

# Phase 4 Verification (Dashboard Analytics, REST API 409 Checks, Admin Tools, 8 Reports)
dotnet run -c Release --no-build -- --verify-phase4
```

---

## 3. Local Demo Accounts & Configuration

Demo accounts are automatically seeded in **Development** mode or when explicit environment variables are provided. In accordance with production security standards, hardcoded passwords have been removed from `appsettings.json`.

### Default Development Credentials
| Role | Email | Password | Assigned Scope |
|---|---|---|---|
| **Admin** | `admin@acxiomcrm.local` | `Admin@Password123!` | Global administration, users, roles, audit logs, all records |
| **Manager** | `manager@acxiomcrm.local` | `Manager@Password123!` | Team CRM records, sales pipeline, management reports |
| **Sales Executive 1** | `sales@acxiomcrm.local` | `Sales@Password123!` | Assigned customers, leads, opportunities, follow-ups |
| **Sales Executive 2** | `sales2@acxiomcrm.local` | `Sales2@Password123!` | Isolated account for cross-account boundary verification |

### Customizing Credentials via Environment Variables
Set these variables in your environment before launch:
```powershell
$env:ADMIN_EMAIL = "admin@yourdomain.com"
$env:ADMIN_PASSWORD = "YourSecurePassword123!"
$env:MANAGER_EMAIL = "manager@yourdomain.com"
$env:MANAGER_PASSWORD = "YourSecurePassword123!"
$env:SALES_EMAIL = "sales@yourdomain.com"
$env:SALES_PASSWORD = "YourSecurePassword123!"
```

---

## 4. Role-Based Access Control (RBAC) Matrix

| Module | Admin | Manager | Sales Executive |
|---|---|---|---|
| **Dashboard** | Full system metrics | Team metrics & pipeline | Assigned records only |
| **Customers** | All records | Team & unassigned records | Assigned records only |
| **Leads** | All records | Team leads | Assigned leads only |
| **Opportunities** | All records | Team opportunities | Assigned deals only |
| **Follow-Ups** | All records | Team follow-ups | Assigned follow-ups only |
| **Activities** | All records | Team activities | Assigned activities only |
| **User Administration** | Full control (reset/unlock/roles) | No access (403) | No access (403) |
| **Role Management** | Full view | No access (403) | No access (403) |
| **Audit Logs** | Complete ledger | Scoped team audit events | No access (403) |
| **Reports (8 Types)** | Full organization data | Team data & metrics | Scoped assigned data |
| **REST APIs** | Authorized endpoints | Authorized endpoints | Authorized endpoints |

---

## 5. Database Architecture & Design

The database schema utilizes additive EF Core migrations targeting SQLite (`AcxiomCRM.db`):
- `20261127083000_InitialCreate`: Identity tables + Customer, Lead, Opportunity, FollowUp, Activity, AuditLog
- `20261127114321_AddPhase2Schema`: Customer and Lead unique normalized indexes and assignment columns
- `20261127140739_AddPhase3Schema`: Soft deletion (`IsDeleted`) and Foreign Keys across entities

### Key Entities
```
 Customer (1) <────── (N) Opportunity (1) <────── (N) FollowUp
    │                          │
    │ (1)                      │ (1)
    ▼ (N)                      ▼ (N)
 Activity                   Activity
    ▲                          ▲
    │ (N)                      │ (N)
  Lead (1) ────────────────────┘
```

- **Customer**: `CustomerId`, `CustomerCode` (Unique), `CustomerName`, `Email`, `NormalizedEmail` (Unique Index), `Phone`, `NormalizedPhone` (Unique Index), `CompanyName`, `Address`, `City`, `State`, `Status`, `AssignedTo`, `CreatedBy`, `CreatedDate`, `IsDeleted`.
- **Lead**: `LeadId`, `LeadCode` (Unique), `LeadName`, `Email`, `Phone`, `CompanyName`, `Source`, `Status` (New, Contacted, Qualified, Unqualified, Converted, Lost), `ExpectedValue`, `AssignedTo`, `CreatedDate`, `ConvertedCustomerId`, `IsDeleted`.
- **Opportunity**: `OpportunityId`, `OpportunityName`, `CustomerId`, `LeadId`, `Amount`, `Stage` (Qualification, Proposal, Negotiation, Won, Lost), `Probability` (0–100), `WeightedValue` (Calculated), `ExpectedCloseDate`, `Status` (Open, Won, Lost), `AssignedTo`, `CreatedDate`, `IsDeleted`.
- **FollowUp**: `FollowUpId`, `CustomerId`, `LeadId`, `OpportunityId`, `FollowUpDate`, `FollowUpType` (Call, Meeting, Email, Task), `Remarks`, `Status` (Planned, Completed, Cancelled), `AssignedTo`, `IsDeleted`.
- **Activity**: `ActivityId`, `ActivityType` (Call, Meeting, Email, Task), `Subject`, `Description`, `ActivityDate`, `CustomerId`, `LeadId`, `OpportunityId`, `AssignedTo`, `Status` (Completed, Pending, InProgress, Cancelled), `IsDeleted`.
- **AuditLog**: `AuditLogId`, `UserId`, `Action`, `EntityName`, `RecordId`, `OldValue`, `NewValue`, `CreatedDate`, `IpAddress`.

---

## 6. REST API Reference

All protected endpoints enforce role-based scoped access. State-changing requests (`POST`, `PUT`, `DELETE`) with cookie authentication validate anti-forgery tokens.

### Authentication Endpoints
- `POST /api/auth/login`: Authenticates user credentials.
  - Request: `{ "email": "admin@acxiomcrm.local", "password": "Admin@Password123!" }`
  - Response: `200 OK` with `{ "email": "...", "name": "...", "role": "..." }`, sets session and `XSRF-TOKEN` cookie.
- `POST /api/auth/logout`: Clears session cookie (`200 OK`).
- `GET /api/auth/csrf`: Returns `{ "token": "...", "headerName": "X-XSRF-TOKEN" }`.

### Customers API (`/api/customers`)
- `GET /api/customers`: Scoped list of active customers; supports `?search=...`.
- `GET /api/customers/{id}`: Customer details; returns `404 Not Found` or `403 Forbidden` if outside scope.
- `POST /api/customers`: Creates a customer.
  - Enforces independent email and 10-digit phone uniqueness. Returns **`409 Conflict`** if either email or phone is already registered.
  - Returns `201 Created` with created `CustomerDto` and `Location` header.
- `PUT /api/customers/{id}`: Updates customer details; returns `409 Conflict` on duplicate collisions; returns `200 OK`.
- `DELETE /api/customers/{id}`: Soft deletes customer (`IsDeleted = true`); returns `200 OK`.

### Leads API (`/api/leads`)
- `GET /api/leads`: Scoped list of leads.
- `POST /api/leads`: Creates a new lead (status `New`, `ExpectedValue > 0`); returns `201 Created`.

### Opportunities API (`/api/opportunities`)
- `GET /api/opportunities`: Scoped list of opportunities.
- `POST /api/opportunities`: Creates an opportunity (`Amount > 0`, `Probability 0–100`, future expected close date for open deals); returns `201 Created`.

### Follow-Ups API (`/api/followups`)
- `GET /api/followups`: Scoped list of follow-ups.
- `POST /api/followups`: Creates a follow-up. New follow-ups with dates earlier than today are rejected with `400 Bad Request` regardless of status. Returns `201 Created`.

### Reports API (`/api/reports`)
- `GET /api/reports/pipeline`: Returns JSON metrics for stage-wise, owner-wise, and weighted open pipeline amounts.

---

## 7. Main Application Demo URLs

| Page / Feature | URL Path | Access Level |
|---|---|---|
| **Dashboard** | `/Dashboard` | Authenticated (Role-scoped KPIs & Chart.js) |
| **Customers** | `/Customers` | Authenticated (List, Create, Edit, Details, Delete) |
| **Leads** | `/Leads` | Authenticated (List, Create, Edit, Convert, Delete) |
| **Opportunities** | `/Opportunities` | Authenticated (Pipeline list, Create, Edit, Outcomes) |
| **Follow-Ups** | `/FollowUps` | Authenticated (Schedule, Overdue, Complete) |
| **Activities** | `/Activities` | Authenticated (Log Call, Meeting, Email, Task) |
| **Reports Hub** | `/Reports` | Authenticated (Directory of all 8 reports) |
| **1. Customer Report** | `/Reports/Customers` | Role-scoped |
| **2. Lead Report** | `/Reports/Leads` | Role-scoped |
| **3. Follow-Up Report** | `/Reports/FollowUps` | Role-scoped |
| **4. Opportunity Report** | `/Reports/Opportunities` | Role-scoped |
| **5. Pipeline Report** | `/Reports/Pipeline` | Role-scoped |
| **6. Conversion Report** | `/Reports/SalesConversion` | Role-scoped |
| **7. User Activity Report** | `/Reports/UserActivity` | Role-scoped |
| **8. Audit Report** | `/Reports/Audit` | Admin & Manager only |
| **User Management** | `/Admin/Users` | Administrator only |
| **Role & Permission Matrix**| `/Admin/Roles` | Administrator only |
| **System Audit Logs** | `/Admin/AuditLogs` | Administrator only |

---

## 8. Final 14-Step Acceptance Scenario Results (Section 17.19)

| Step | Acceptance Scenario Check | Verification Result | Status |
|---|---|---|:---:|
| **1** | Open without authentication $\rightarrow$ protected pages blocked | Anonymous requests redirect to `/Account/Login` | **PASSED** |
| **2** | Register/login with valid user $\rightarrow$ user reaches Dashboard | Authenticated redirect lands on `/Dashboard` | **PASSED** |
| **3** | Create Customer with invalid email/phone $\rightarrow$ client validation blocks save | Client-side unobtrusive validation blocks submit | **PASSED** |
| **4** | Bypass browser validation with crafted request $\rightarrow$ server rejects invalid data | Direct HTTP POST rejected by server model validation | **PASSED** |
| **5** | Create Opportunity with Amount $\le 0 \rightarrow$ rejected | Amount $0$ and $-1$ rejected on client and server | **PASSED** |
| **6** | Create Opportunity with Probability $> 100 \rightarrow$ rejected | Probability $101$ rejected on client and server | **PASSED** |
| **7** | Set Expected Close Date in the past $\rightarrow$ rejected for active opportunity | Past close date rejected with clear error message | **PASSED** |
| **8** | Create Follow-Up dated before today $\rightarrow$ rejected for new follow-up | Past date rejected through MVC and API for any status | **PASSED** |
| **9** | Login as SalesExecutive $\rightarrow$ only assigned sales scope accessible | Strict record isolation between Sales 1 and Sales 2 | **PASSED** |
| **10** | Login as Manager $\rightarrow$ team pipeline/report access available | Manager accesses team reports; Admin pages return 403 | **PASSED** |
| **11** | Login as Admin $\rightarrow$ user/role/audit administration available | Full user edit, lockout unlock, password reset, and logs | **PASSED** |
| **12** | Create/update/delete a CRM record $\rightarrow$ audit entry generated | Captured with UserId, Action, RecordId, Old/NewValue | **PASSED** |
| **13** | Call `/api/customers` $\rightarrow$ authorized JSON response returned | Returns scoped JSON; duplicate returns `409 Conflict` | **PASSED** |
| **14** | Open Dashboard $\rightarrow$ KPI cards and Chart.js charts display authorized data | 9 KPI cards & 3 Chart.js charts render authorized data | **PASSED** |

---

## 9. Packaging & Submission Verification

- Project archive: `AcxiomCRM_Submission.zip`
- Includes complete source code, project file, EF Core migrations, Razor views, CSS/JS static assets, and documentation.
- Excludes `bin/`, `obj/`, `.git/`, local `.db` files, task logs, and environment secrets.
- Verified clean extraction and standalone execution against a fresh SQLite database.
