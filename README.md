# TMP Chat Bot

A simple chat bot for **TruckersMP** (ETS2 / ATS).  
This bot reads your chat log file and can respond with useful commands.  
It also connects to **Groq API (LLaMA model)** for AI answers.

---

## ✨ Features
- `!gpt <text>` → ask AI (LLaMA model via Groq API). The bot will answer in the same language as the question.  
- Works inside the TruckersMP chat (reads the log file).  
- Filters messages using a **banned words list** to prevent inappropriate content.  
- Lightweight and easy to use.  

---

## 🚀 Usage

1. Download the following files:
   - `tmp-bot.exe`
   - `banned_words.txt`

2. Place both files in the same folder (recommended: Desktop).

3. Run `tmp-bot.exe`.

4. On first launch, the app will ask for:
   - **Groq API key** (create one at [Groq Console](https://console.groq.com/keys))
   - **Path to `banned_words.txt`**  
     Example:
     ```
     C:\Users\YourName\Desktop\banned_words.txt
     ```

5. The app will save this configuration automatically. On subsequent launches, no input is required.

---

## 🔹 Bot Commands (examples)

`!help`
```bash
User: !help
Bot: 🤖 Hello! I am PolyakoffBot v3, ready to assist you. Commands: !help, !gpt <question>, !serverinfo, !players, !version, !socials, !events.
```

`!gpt <your message>`
```bash
User: !gpt who is best driver in truckersmp?
Bot: 🤖 GPT: Hard to say! Many players are skilled, but everyone has their own style. 🚛
```

`!serverinfo`
```bash
User: !serverinfo
Bot: 🖥️ Server: Simulation 1 | 145.239.0.11:443 | Players: 2500/3500 | Queue: 35
```

`!players`
```bash
User: !players
Bot: 👥 Total players online (all servers): 5421
```

`!version`
```bash
User: !version
Bot: 📦 Supported ETS2 version: 1.52.1 | Supported ATS: 1.52.1
```

`!socials`
```bash
User: !socials
Bot: 🔗 My Discord Nickname: polyakoff | Github: github.com/GitPolyakoff |
```

`!events`
```bash
User: !events
Bot: 📅 Events now/soon: Real Operations at 2025-09-20 | Convoy Community Event at 2025-09-25
```

---

## 🛡 Content Filtering

- Detects full words and word fragments
- Ignores symbols, spaces, numbers, and obfuscation (`f@ck`, `n1gga`, `sh!t`, etc.)
- Works in multiple languages
- Ensures the bot never outputs prohibited words
  
---

## 👥 Credits

**Developers:**
- **polyakoff** - Main developer & project creator
- **lrnsxgod** - Contributor

**GitHub Profiles:**
- [GitPolyakoff](https://github.com/GitPolyakoff)
- [lrnsxdev](https://github.com/lrnsxdev)

**Discord:**
- polyakoff & lrnsxgod

---


## 📌 Notes

- Answers from GPT are short (1–2 sentences), because game chat has limited space.

- The bot only works while the log file is updating (so you must be in TruckersMP).

- The app automatically saves the API key and banned words path on first launch for future use.

---
