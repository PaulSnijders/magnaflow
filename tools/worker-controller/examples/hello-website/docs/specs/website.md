# Spec: Hello Website

A minimal static website, plain HTML/CSS, no frameworks, no build tooling.

## Requirements

- All pages live in `src/`.
- Every page is valid HTML5: `<!doctype html>`, `<html lang="en">`, `<head>` with
  `<meta charset="utf-8">`, a `<title>`, and a `<body>`.
- Shared styling lives in `src/style.css` and is linked from every page.
- Pages link to each other through a simple `<nav>` at the top.
- Keep it small and readable; no JavaScript unless a task explicitly asks for it.
