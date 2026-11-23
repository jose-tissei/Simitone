![image](https://user-images.githubusercontent.com/6294155/29047328-2bb60b54-7bc3-11e7-8d88-cee5309495ed.png)

[Latest Pre-release](https://github.com/riperiperi/Simitone/releases/latest/) | [Download](https://github.com/riperiperi/Simitone/releases/latest/download/SimitoneWindows.zip)

Alternative C# Windows Frontend for The Sims 1, based off of FreeSO. http://freeso.org 
(***REQUIRES*** a legitimate copy of The Sims: Complete Collection)

![image](https://user-images.githubusercontent.com/6294155/68995217-112b2680-0883-11ea-9f92-1acc839a7ec0.png)

NOTE! Currently does not support the entire Fame career track, saving on vacation and a few other important things. While all objects run, many of them have bugs that can make certain lot types unplayable. For current development progress, see [this issue.](https://github.com/riperiperi/Simitone/issues/8)

*Only for Desktop Windows.* Other platforms cannot be officially supported.

# Purpose

On modern operating systems, The Sims has a few nagging issues that make it less than playable. Simitone can be seen as a tool to improve the playability of your existing installation of The Sims. Here are some features:

- Playable in 3D - using a combination of models generated from the 2D sprites and created by the community.
  - [Download the mesh pack here!](http://forum.freeso.org/forums/3d-remeshing.40/)
- Custom user interface that works at modern resolutions. Working on a more desktop oriented interface.
- Improved graphical performance, support for high resolutions and refresh rates.
- Custom lighting - directional lights with smooth falloffs and shadows using generated 3D meshes.
- *Volcanic*, a program which allows you to examine, modify and create new game objects. (from FreeSO)

# AI Conversations & Sim Memory Mod

This fork includes an optional **AI Conversations & Sim Memory** mod (`Tools/Conversations`) powered by a bundled local [Laya](https://github.com/laya-ai) model (runs offline on CPU, no live LLM at play time):

- **Autonomous parallel room conversations:** awake adult Sims in the same room automatically chat with each other **without interrupting their ongoing activities** whenever their Social need drops to `<= 60%`.
- **Laya-driven social intelligence:** chooses verbal interactions and AI talk subjects based on each Sim's knowledge of the listener, skills (Charisma, Cooking, Mechanical, Creativity, Logic, Body), moods, and past social history.
- **Per-Sim Social Memory & floating dossier window:** every conversation records what a Sim has learned about each Sim they know — **Daily / Lifetime** relationship, familiarity %, revealed personality traits, observed temperament, career, liked/disliked interests, discovered skills, and discussed topics — accessible from the **Sim Memory** button in the selected Sim's Live-mode panel (right alongside Needs, Career, Personality, Relationships, and Inventory).
- **Simplified manual pie menu & 100% AI Talk subjects:** clicking another adult Sim shows one **Talk** bubble and a **Romance** submenu next to the usual interactions (Options: "Pie menu: Simplified / Full"); Talk presents 3 Laya-ranked AI subjects rather than raw legacy interest labels.
- **Easy toggle:** enable or disable the entire mod anytime via **Options -> AI conversations** (green = on).

See [Tools/Conversations/README.md](Tools/Conversations/README.md) for architecture, Laya bundling, and installation instructions (`pwsh Tools/Install-Conversations.ps1`).

## Screenshots

### 1. Simplified Manual Pie Menu (Talk, Romance and the usual interactions)
![Simplified Manual Pie Menu](docs/conversations/screenshots/01-pie-menu-talk-romance.jpg)

### 2. AI Talk Subject Chooser
![AI Talk Subject Chooser](docs/conversations/screenshots/02-ai-talk-subjects-dialog.jpg)

### 3. Live Over-Head Speech Balloons
![Live Speech Balloons](docs/conversations/screenshots/03-ai-talk-speech-balloons.jpg)

### 4. Autonomous Parallel Room Conversations
![Autonomous Parallel Room Conversations](docs/conversations/screenshots/04-parallel-autonomous-conversations.jpg)

### 5. Selected Sim Live Panel — Sim Memory Button
![Selected Sim Live Switcher](docs/conversations/screenshots/05-live-sim-memory-switcher.jpg)

### 6. Floating Sim Memory Dossier Window
![Floating Sim Memory Dossier Window](docs/conversations/screenshots/06-sim-memory-dossier-window.jpg)

# Why is it called Simitone?

Simitone -> Semitone -> musical term -> C# -> a note

Further questions can be directed at my PR manager, uh, ... burglar cop.
