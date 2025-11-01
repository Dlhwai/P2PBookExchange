# P2P Book Exchange Platform

A backend API system that enables peer-to-peer book sharing and exchanges.
Built using **ASP.NET Core Minimal API**, **PostgreSQL**, **Npgsql**, and **BCrypt**.

---

## Overview

This system supports:

- **User account creation, login, and profile updates**
- **Book creation, update, and search**
- **Trade requests between users**
- **Accepting or rejecting trade requests**
- **Automatic book ownership transfer when a trade is accepted**
- **Automatic invalidation of other related trade requests**
- **User point system with multipliers**
- **Point history records**
- **User genre preferences**

---

## Features

### 1. User Management
- Local authentication with **BCrypt** password hashing
- Update profile information
- Manage genre preferences:
  - Add genres
  - Delete genres
  - List user’s selected genres

### 2. Book Management
- Create new book listings
- Update book details
- Search by keyword, user ID, or publication year range
- Filter by:
  - Title
  - Subtitle
  - Description
  - Condition
  - Location
  - Genre
  - Status
  - Author name

### 3. Trade System
- Request to exchange a book with another user
- Optional **offered book** by requester
- Owner can **accept** or **reject** the trade
- When a trade is **accepted**:
  - Book ownership automatically transferred
  - All other trades involving the same books are marked inactive (`use_yn = false`)
  - Points awarded to both users
  - A point history record is inserted

### 4. Points and Multipliers
- Base points awarded for completed exchanges
- Multiplier determined by user’s reputation
- Multipliers stored in the database
- Full point earning history available for each user

---
## Database Installation & Restore Guide

### 1. Install PostgreSQL

#### macOS (Postgres.app)
Download: https://postgresapp.com/
```
pg_ctl -D ~/Library/Application\ Support/Postgres/var-18 start
```

#### macOS (Homebrew)
```
brew install postgresql@18
brew services start postgresql@18
```

#### Windows (EnterpriseDB Installer)
Download: https://www.postgresql.org/download/windows/

#### Linux (Ubuntu/Debian)
```
sudo apt update
sudo apt install postgresql postgresql-contrib
sudo service postgresql start
```

---

### 2. Create a Database
```
createdb p2pbook
```
If needed:
```
psql -U postgres -c "CREATE DATABASE p2pbook OWNER postgres;"
```

---

### 3. (Optional) Set Up Passwordless Restore

#### macOS/Linux
Create:
```
~/.pgpass
```
Content:
```
localhost:5432:p2pbook:postgres:<yourpassword>
```
Permissions:
```
chmod 600 ~/.pgpass
```

#### Windows
Create:
```
%APPDATA%/postgresql/pgpass.conf
```
Same content.

---

### 4. Restore From Database Dump

#### Custom Format (.dump)
```
pg_restore -d p2pbook /path/to/dump-p2pbook.dump
```
Parallel restore:
```
pg_restore -j 4 -d p2pbook /path/to/dump-p2pbook.dump
```

#### SQL File (.sql)
```
psql -d p2pbook -f /path/to/dump-p2pbook.sql
```

---

### 5. Verify the Restore
```
psql p2pbook
\dt
```
You should see all tables under the **bookx** schema.

---

### 6. Configure API Connection
Update your `appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=p2pbook;Username=postgres;Password=yourpassword"
}
```

---

### Quick Restore Commands
```
createdb p2pbook
pg_restore -d p2pbook dump-p2pbook.dump
```
