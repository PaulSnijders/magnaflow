# mf-cockpit — Examples

A smoke test with a throwaway project. The dashboard pages need no AI
agent; §3 runs the chat against a stub. Commands are PowerShell on Windows.

## 0. Build

```powershell
cd C:\GIT\magnaflow\tools\mf-cockpit
dotnet build
```

## 1. A throwaway project

```powershell
mkdir C:\tmp\cockpit-demo\proj\docs\prompts -Force
mkdir C:\tmp\cockpit-demo\proj\docs\specs -Force
cd C:\tmp\cockpit-demo\proj
git init -b main
git config user.email you@example.com
git config user.name you

@'
---
title: Say hello
status: ready
attempts: 0
---

## Goal
Do something useful.
'@ | Set-Content docs\prompts\0001-cmd-hello.md

@'
# Technical

Demo overview spec.
'@ | Set-Content docs\specs\_overview.md

git add -A
git commit -m "seed"
```

## 2. Point mf-cockpit at it

```powershell
@'
projects:
  - name: demo
    path: C:/tmp/cockpit-demo/proj
'@ | Set-Content C:\tmp\cockpit-demo\mf-cockpit.yml

C:\GIT\magnaflow\tools\mf-cockpit\src\MagnaFlow.MfCockpit\bin\Debug\net10.0\MagnaFlow.MfCockpit.exe --config C:\tmp\cockpit-demo\mf-cockpit.yml
```

Open `http://localhost:5210/`: `demo` with one `ready` command and an
empty attention list. `project.html?p=demo` shows the lane table, git
branch and a "new draft command" form; `specs.html?p=demo` browses the
spec tree.

## 3. Chat against a stub agent

`chat.command` is swappable, like the agent command in the worker. A
minimal stub that echoes one stream-json reply:

```powershell
@'
@echo off
echo {"type":"system","subtype":"init"}
echo {"type":"result","subtype":"success","session_id":"demo-session","result":"hello from the stub"}
exit /b 0
'@ | Set-Content C:\tmp\cockpit-demo\chat-stub.cmd

@'
projects:
  - name: demo
    path: C:/tmp/cockpit-demo/proj
chat:
  command: C:/tmp/cockpit-demo/chat-stub.cmd
'@ | Set-Content C:\tmp\cockpit-demo\mf-cockpit.yml
```

Restart mf-cockpit, open `chat.html?p=demo` and send a message. The reply
streams in; "make this a command" turns it into a new `draft` cmd file,
committed immediately.

## 4. The only two writes, from the browser

- "New draft command" on `project.html` → `POST /api/projects/demo/commands`
  → a new `NNNN-cmd-*.md` with `status: draft`, committed as
  `cockpit: create draft NNNN-name`.
- "Make ready" on a `draft` row → `POST .../commands/{id}/ready` → flips to
  `status: ready`, committed as `cockpit: ready NNNN-name`. Any other
  current status gets a `409`.

```powershell
git log --oneline   # both bookkeeping commits, same as a human editing the files by hand
```
