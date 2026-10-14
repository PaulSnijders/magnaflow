@echo off
rem Action git-status: show branch, sync state and working-tree status.
git status --short --branch
git log --oneline -5
