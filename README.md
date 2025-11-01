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

