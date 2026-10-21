# ComfyGenReader — Specs

Lightweight, portable Windows desktop app that shows ComfyUI (Stable Diffusion) metadata embedded in generated images and videos.

## Platform
- .NET 10, Windows desktop.
- Portable: the published folder can be zipped, copied to another PC and run. No installer, no code signing.
- Third-party DLLs/binaries live in a folder under the app root (`lib\`) where possible.
- Third-party libraries must have no known critical vulnerabilities.
- History and future settings are JSON files stored with the app files.

## File types
- Images: `.jpg`, `.jpeg`, `.jpe`, `.jfif`, `.webp`, `.png`
- Videos: `.mp4`, `.webm`, `.avi`
- All other types are ignored.

## Window
- Min size 400×400 px, no max.
- Three frames: **Top** (full width), **Left** (60% width, under Top), **Right** (40% width, under Top).

## Top frame
- Editable path box + **Browse** button + **GO** button.
- Below: dropdown of the 10 most recent files, 100% width.
- History includes files that were browsed to, typed/pasted into the path, dragged in, or picked from the dropdown. Files reached with Previous/Next are **not** added.

## Left frame
- Shows the open image/video. With no file open: a "Drag here" message with an icon.
- Files can be dropped onto it; the path box updates to the dropped file.
- Bottom: **Previous** / **Next** buttons step through supported files in the same folder.
- Images are scaled to fill the space without distortion (fit to the limiting edge).
- Videos: Play, Stop, a seek bar that shows a frame preview while scrubbing, Mute/Unmute (muted by default). Scaled like images.

## Right frame
- **Highlights** section: Seed, and Prompt (multiline, 5 lines tall, scroll bar).
- **Raw** section: all metadata as pretty-printed JSON; escape codes (`\n`, `\"`, `\uXXXX`, …) shown as real characters.
- Small **Export** button below Raw: saves the metadata as a valid `.json` file, intended to be importable into ComfyUI as a workflow.

## Errors
- The app must not crash. A global try/catch routes every error to the Raw section: exception message first, then the stack trace.
