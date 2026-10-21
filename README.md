# ComfyGenReader

A small, portable Windows app for reading the generation metadata that ComfyUI embeds in its images and videos.

Open or drag in a file (`.png`, `.jpg`, `.webp`, `.mp4`, `.webm`, `.avi`) to preview it and see its seed, prompt and full metadata JSON. You can export the workflow as a `.json` file to load back into ComfyUI. Previous/Next buttons step through the other files in the same folder.

## Download

Get the latest `ComfyGenReader-<version>-win-x64.zip` from [Releases](https://github.com/john-elrod/ComfyGenReader/releases), unzip it anywhere and run `ComfyGenReader.exe`. Nothing needs to be installed.

## Build

Requires the .NET 10 SDK on Windows:

```
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
```

The zip is written to `artifacts\`.
