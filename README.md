# ECL eBioServer Biometric & Attendance Management System

Enterprise Biometric & Attendance Management System for Eastern Coalfields Limited (ECL) integrating eBioServer turnstile hardware, automated biometric synchronization, AES-256 encrypted push webhooks, and sub-area attendance monitoring.

---

## 🛠️ Tech Stack
- **Framework**: .NET 10 (C#) / Blazor Web App (Interactive Server)
- **Database**: PostgreSQL (via Entity Framework Core & Npgsql)
- **Biometric Integration**: eBioServer SOAP ASMX Web Services (`UpdateLocation`, `UpdateDevice`, `UpdateEmployee`)
- **Push Webhook**: High-throughput REST endpoint supporting raw JSON & AES-256-CBC encrypted payloads with 32-character key
- **UI / Styling**: Bootstrap 5 + Bootstrap Icons + Custom Enterprise Dark/Light Styling

---

## 🚀 Quick Start Guide

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [PostgreSQL](https://www.postgresql.org/) (Running on port 5432)
- Node.js & npm (optional, for local webhook tunneling)

### 2. Configure Environment Secrets
Copy `.env.example` to `.env`:
```bash
cp .env.example .env
```
Open `.env` and fill in your actual credentials:
```env
# PostgreSQL Database Connection
DB_CONNECTION_STRING=Host=localhost;Port=5432;Database=ecl_ebios;Username=postgres;Password=YOUR_POSTGRES_PASSWORD;Include Error Detail=true

# eBioServer SOAP API Settings
EBIOS_URL=http://YOUR_EBIOS_SERVER_IP:82/webservice.asmx
EBIOS_USERNAME=essl
EBIOS_PASSWORD=YOUR_EBIOS_PASSWORD

# eBioServer Webhook Decryption Key (32-character AES-256 key)
EBIOS_WEBHOOK_KEY=essl1234111111111111111111111111
```

> **Note**: The `.env` file is listed in `.gitignore` and is never committed to GitHub.

### 3. Build & Run
```bash
dotnet build EbiosApp.slnx
dotnet run --project src/Ebios.Web/Ebios.Web.csproj
```
Open your browser at `http://localhost:5248`.

---

## 📡 Push Webhook Testing

### Webhook Specification:
- **Endpoint**: `POST /api/webhook/attendance`
- **Content-Type**: `application/json`
- **Return Response**: `Success` (plain text)
- **Payload Support**:
  1. Plain JSON object or array of punches
  2. Encrypted payload: `{"data": "<AES-256-CBC-Base64>"}`

### Live Testing with Remote eBioServer (via Public Tunnel):
Because eBioServer runs on a remote server/IP, it cannot directly reach `localhost:5248`. To expose your local dev server for live testing:

1. In a separate terminal, start a free HTTPS tunnel:
   ```bash
   npx localtunnel --port 5248
   ```
2. Copy the resulting public URL (e.g. `https://XYZ.loca.lt`).
3. In **eBioServer Desktop Utility**:
   - Navigate to **Utilities** -> **Web Hook Settings**.
   - Set **Web Hook URL**: `https://XYZ.loca.lt/api/webhook/attendance`
   - Encryption: Enable or Disable (both are supported).
   - If Encryption is Enabled: Set Password to 32 characters (e.g. `essl1234111111111111111111111111`).
   - Click **Save**.
4. Punch on any registered SilkBio or Face/Finger turnstile device.
5. Watch the punch appear instantly in real-time under the **Attendance & Logs** console (`/attendance`).

