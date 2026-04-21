# MusicColab MVP — Quick Start

## 🚀 Two-Command Launch

**Terminal 1: Backend**
```bash
cd /home/phlawless/dev/MusicColab/MusicColab.Api
dotnet run --urls http://localhost:5150
```

**Terminal 2: Frontend**
```bash
cd /home/phlawless/dev/MusicColab/musiccolab-web
npm run dev
```

**Open:** http://localhost:5173

---

## 📝 Test Credentials

Use these pre-seeded accounts to explore:

| Email | Password |
|-------|----------|
| alice@example.com | Password123! |
| bob@example.com | Password123! |

---

## 🎵 What You Can Do

### Swipe Tab
- Drag artist cards left (Dislike) or right (Like)
- Click buttons for keyboard/mobile users
- See your preferences saved in real-time

### Compare Tab
- Select another user from the list
- Click "Run comparison"
- View:
  - **Score** (0–100) based on taste overlap
  - **Shared likes** (artists you both enjoy)
  - **Conflicts** (opposite preferences)
  - **Discovery** (new artists from their taste)

---

## 🏗️ Architecture Snapshot

| Layer | Technology | Location |
|-------|-----------|----------|
| **Frontend** | Svelte 5, TypeScript, Vite | `musiccolab-web/` |
| **Backend** | ASP.NET Core 10, C# | `MusicColab.Api/` |
| **Database** | SQLite | `MusicColab.Api/musiccolab.dev.db` |
| **Auth** | JWT Bearer tokens | In-memory, localStorage |

---

## 📡 Key APIs

| Method | Endpoint | Purpose |
|--------|----------|---------|
| POST | `/auth/register` | Create account |
| POST | `/auth/login` | Sign in |
| GET | `/artists/feed?limit=12` | Get swipe cards |
| POST | `/preferences` | Save like/dislike |
| GET | `/users` | List all users |
| GET | `/compare/{userA}/{userB}` | Compute compatibility |

---

## ⚙️ Configuration

**Spotify (Optional):**
- Edit `MusicColab.Api/appsettings.Development.json`
- Add `Spotify.ClientId` and `Spotify.ClientSecret`
- App works fine without it (uses seeded artists)

**API Base URL:**
- Default: `http://localhost:5150`
- Override: `VITE_API_BASE_URL=...npm run dev`

---

## 📊 Seeded Data

- **2 test users** ready to login
- **5 popular artists** in database to swipe
- **Empty preferences** — build your own taste profile

---

## 🐛 Common Issues

**Port in use?**
```bash
lsof -ti:5150 | xargs kill -9  # Clear backend
npm run dev --port 5174         # Use different frontend port
```

**Corrupted database?**
```bash
rm -f MusicColab.Api/musiccolab.dev.db*
# Restart backend to rebuild
```

**CORS error?**
- Backend must be on 5150 (or update `src/lib/api.ts`)
- Frontend must be able to reach backend URL

---

## 📚 Full Documentation

See **README.md** in this directory for complete architecture, API docs, design notes, and extension ideas.
