# FPC repository instructions

## Scope

- This repository contains the Flesh Physics Controller BepInEx 5 plugin for Koikatu / Koikatsu Party.
- Preserve the plugin GUID `codex.koikatumanager.thighphysicscontroller` and card-data compatibility.
- Do not restore the removed `Soft.xml`, `Realistic.xml`, or `Exaggerated.xml` built-in presets.
- Keep the default UI focused on Strength, Softness, Motion response, and explicit Spring/Chain mode choices.

## Required verification

- Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FleshParameterModel.ps1` after parameter, preset, serialization, solver-math, or UI-mapping changes.
- Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-ThighPhysicsController.ps1 -Force` before producing a release.
- Use the sandbox regression scripts for changes that affect runtime integration, safety, presets, or frame-rate behavior.
- Treat old Mono `mono_heap_delta=0` as no observed heap growth, not proof of exact zero allocation.

## Repository hygiene

- Do not commit `artifacts/`, `backups/`, `packaging/`, `bin/`, or `obj/`.
- Do not install into the main game directory unless the user explicitly requests installation.
- Keep release version strings synchronized in the plugin attribute, build script, README, changelog, and development notes.
- Work on a feature branch and open a pull request to `main`; do not push directly to `main` unless explicitly requested.
