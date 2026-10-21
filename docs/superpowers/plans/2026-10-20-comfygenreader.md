# ComfyGenReader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a portable .NET 10 WPF app that previews ComfyUI-generated images/videos and shows their embedded generation metadata (seed, prompt, raw JSON) with JSON export.

**Architecture:** A UI-free class library (`ComfyGenReader.Core`) holds all logic: metadata reading, JSON normalization/formatting, ComfyUI field extraction, history, folder navigation, and a `ReaderSession` view-model. The WPF app (`ComfyGenReader`) is a thin shell that binds to `ReaderSession`, renders images with WPF and videos with LibVLC. Video metadata and scrub-preview frames come from bundled `ffprobe`/`ffmpeg` executables.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, xUnit v3, MetadataExtractor (image EXIF/XMP), LibVLCSharp.WPF + VideoLAN.LibVLC.Windows (video playback), FFmpeg LGPL shared build (ffprobe/ffmpeg exes).

**Spec:** [Specs.md](../../../Specs.md) (derived from `MySpecs.md`)

## Global Constraints

- Target framework: `net10.0-windows`, runtime `win-x64`. Publish self-contained, single-file exe (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`), not trimmed.
- Portable layout: `ComfyGenReader.exe`, `data\` (runtime JSON), `lib\libvlc\`, `lib\ffmpeg\`. Nothing written outside the app folder except .NET's single-file extraction cache.
- Supported extensions (case-insensitive): images `.jpg .jpeg .jpe .jfif .webp .png`; videos `.mp4 .webm .avi`. Everything else is ignored.
- Window `MinWidth=400`, `MinHeight=400`, no max. Left/right column ratio `6*` / `4*`.
- Recent-files list capacity: 10. Stored in `data\history.json` next to the exe.
- Video is muted by default.
- No third-party package may have a known vulnerability: every project sets `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`, and `WarningsAsErrors` includes `NU1901;NU1902;NU1903;NU1904`. FFmpeg and LibVLC versions are checked manually against NVD (Task 11).
- Unhandled exceptions never terminate the app; they appear in the Raw panel as `Type: Message`, blank line, stack trace (inner exceptions appended the same way).

## Review Focus

1. **Python-style `NaN` / `Infinity` in metadata JSON** (ComfyUI writes these via `json.dumps`) — must parse, with those tokens becoming `null`, instead of falling back to raw text. → Task 2 test `TryParseLenient_ReplacesBareNaNAndInfinity`.
2. **Path pasted with surrounding quotes/whitespace** (Explorer "Copy as path" yields `"C:\x\a.jpg"`) — must open the file. → Task 8 test `Open_TrimsQuotesAndWhitespace`.
3. **Seed or prompt text supplied by a link to another node** (widget converted to input: `"seed": ["12", 0]`) — must resolve to the linked node's value. → Task 4 tests `Seed_FollowsLinkToPrimitive`, `Prompt_FollowsTextLink`.
4. **`history.json` missing, corrupt, or app folder read-only** — app starts with an empty list and keeps working; save failures are swallowed. → Task 7 tests `Load_CorruptFile_StartsEmpty`, `Add_WhenSaveFails_StillUpdatesInMemory`.
5. **Image with no ComfyUI metadata** (e.g. a camera JPEG) — Raw shows whatever tags exist, Seed/Prompt empty, Export disabled. → Task 3 test `Parse_NoComfyData_ExportIsNull`, Task 8 test `Open_NoComfyData_DisablesExport`.

---

## File Structure

```
ComfyGenReader.sln
Directory.Build.props                 # TFM, nullable, NuGet audit settings for all projects
.gitignore                            # bin/ obj/ artifacts/ third_party/ffmpeg/
src/ComfyGenReader.Core/
  SupportedFiles.cs                   # extension lists, MediaKind, dialog filter
  Json/JsonText.cs                    # lenient parse, embedded-JSON expansion
  Json/DisplayFormatter.cs            # pretty text with unescaped strings
  ErrorFormatter.cs
  Metadata/MetadataResult.cs
  Metadata/MetadataParser.cs          # raw tags -> MetadataResult
  Metadata/ComfyFields.cs             # seed/prompt extraction
  Metadata/IMetadataLoader.cs         # + MetadataLoader (dispatch by kind)
  Metadata/ImageTagReader.cs          # MetadataExtractor
  Media/FfmpegTools.cs                # paths, ffprobe tags, frame grab
  RecentFiles.cs
  FolderNavigator.cs
  ReaderSession.cs                    # view-model
src/ComfyGenReader/                   # WPF app
  App.xaml(.cs)                       # global exception handlers
  MainWindow.xaml(.cs)                # layout, browse, drag/drop, export
  Controls/VideoPlayer.xaml(.cs)      # LibVLC view + controls + scrub preview
tests/ComfyGenReader.Core.Tests/
  Fixtures/ExifFixtures.cs            # writes minimal JPEG/WebP with EXIF strings
  Fixtures/ComfySamples.cs            # sample API prompt + workflow JSON strings
  *Tests.cs
tools/get-ffmpeg.ps1, tools/ffmpeg.lock.json, tools/publish.ps1
third_party/ffmpeg/                   # downloaded, gitignored
```

---

### Task 1: Solution scaffold and supported file types

**Files:**
- Create: `ComfyGenReader.sln`, `Directory.Build.props`, `.gitignore`, `src/ComfyGenReader.Core/ComfyGenReader.Core.csproj`, `src/ComfyGenReader.Core/SupportedFiles.cs`, `src/ComfyGenReader/ComfyGenReader.csproj` (WPF template, references Core), `tests/ComfyGenReader.Core.Tests/ComfyGenReader.Core.Tests.csproj` (xUnit v3, references Core)
- Test: `tests/ComfyGenReader.Core.Tests/SupportedFilesTests.cs`

**Interfaces:**
- Produces: `enum MediaKind { Unsupported, Image, Video }`; `static class SupportedFiles { MediaKind GetKind(string path); bool IsSupported(string path); string DialogFilter { get; } }`

- [ ] **Step 1:** Create the solution with `dotnet new sln`, `dotnet new classlib`, `dotnet new wpf`, `dotnet new xunit3` (install template `xunit.v3.templates` if missing). `Directory.Build.props` sets `TargetFramework=net10.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`, `RuntimeIdentifier=win-x64`, plus the NuGet audit properties from Global Constraints. `.gitignore`: `bin/ obj/ artifacts/ third_party/ffmpeg/ *.user .vs/`.

- [ ] **Step 2: Write the failing tests**

```csharp
[Theory]
[InlineData(@"C:\a\x.JPG", MediaKind.Image)]
[InlineData(@"C:\a\x.jpeg", MediaKind.Image)]
[InlineData(@"C:\a\x.jpe", MediaKind.Image)]
[InlineData(@"C:\a\x.jfif", MediaKind.Image)]
[InlineData(@"C:\a\x.webp", MediaKind.Image)]
[InlineData(@"C:\a\x.mp4", MediaKind.Video)]
[InlineData(@"C:\a\x.WebM", MediaKind.Video)]
[InlineData(@"C:\a\x.avi", MediaKind.Video)]
[InlineData(@"C:\a\x.PNG", MediaKind.Image)]
[InlineData(@"C:\a\x.gif", MediaKind.Unsupported)]
[InlineData(@"C:\a\noext", MediaKind.Unsupported)]
public void GetKind_ByExtension(string path, MediaKind expected) =>
    Assert.Equal(expected, SupportedFiles.GetKind(path));

[Fact]
public void DialogFilter_ListsAllExtensions() =>
    Assert.Equal("Supported media|*.jpg;*.jpeg;*.jpe;*.jfif;*.webp;*.png;*.mp4;*.webm;*.avi",
                 SupportedFiles.DialogFilter);
```

- [ ] **Step 3:** Run `dotnet test --filter FullyQualifiedName~SupportedFilesTests` — expect build FAIL (`SupportedFiles` not defined).
- [ ] **Step 4:** Implement `SupportedFiles` in `src/ComfyGenReader.Core/SupportedFiles.cs` using two `FrozenSet<string>` with `StringComparer.OrdinalIgnoreCase`.
- [ ] **Step 5:** Run the same command — expect PASS. Run `dotnet build` on the solution — expect 0 warnings, 0 errors.
- [ ] **Step 6:** Commit: `git add -A && git commit -m "feat: scaffold solution and supported file types"`

---

### Task 2: JSON text utilities and error formatting

**Files:**
- Create: `src/ComfyGenReader.Core/Json/JsonText.cs`, `src/ComfyGenReader.Core/Json/DisplayFormatter.cs`, `src/ComfyGenReader.Core/ErrorFormatter.cs`
- Test: `tests/ComfyGenReader.Core.Tests/JsonTextTests.cs`, `DisplayFormatterTests.cs`, `ErrorFormatterTests.cs`

**Interfaces:**
- Produces:
  - `static bool JsonText.TryParseLenient(string text, out JsonNode? node)` — succeeds only for a top-level object or array; bare `NaN`, `Infinity`, `-Infinity` tokens **outside string literals** become `null` before parsing.
  - `static JsonNode? JsonText.ExpandEmbedded(JsonNode? node)` — returns a deep copy where every string value that `TryParseLenient` accepts is replaced by the parsed node, recursively (max depth 8).
  - `static string JsonText.ToExportJson(JsonNode node)` — indented, valid JSON (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`).
  - `static string DisplayFormatter.ToDisplayText(JsonNode? node)` — 2-space indented JSON-like text where string values are written **unescaped** (real newlines, tabs, quotes, Unicode) inside double quotes; `null` input → empty string.
  - `static string ErrorFormatter.Format(Exception ex)`

- [ ] **Step 1: Write the failing tests**

```csharp
// JsonTextTests
[Fact] public void TryParseLenient_ReplacesBareNaNAndInfinity()
{
    Assert.True(JsonText.TryParseLenient("""{"a":NaN,"b":-Infinity,"c":"NaN","d":[Infinity]}""", out var n));
    Assert.Null(n!["a"]); Assert.Null(n["b"]); Assert.Equal("NaN", (string)n["c"]!); Assert.Null(n["d"]![0]);
}
[Fact] public void TryParseLenient_RejectsScalarsAndGarbage()
{
    Assert.False(JsonText.TryParseLenient("42", out _));
    Assert.False(JsonText.TryParseLenient("prompt: hi", out _));
}
[Fact] public void ExpandEmbedded_ParsesDoubleEncodedJson()
{
    var n = JsonNode.Parse("""{"prompt":"{\"3\":{\"class_type\":\"KSampler\"}}","note":"plain"}""");
    var x = JsonText.ExpandEmbedded(n)!;
    Assert.Equal("KSampler", (string)x["prompt"]!["3"]!["class_type"]!);
    Assert.Equal("plain", (string)x["note"]!);
}

// DisplayFormatterTests
[Fact] public void ToDisplayText_UnescapesStrings()
{
    var n = JsonNode.Parse("""{"t":"line1\nsay \"hi\" \u00e9 \ud83d\ude00"}""");
    Assert.Equal("{\n  \"t\": \"line1\nsay \"hi\" é 😀\"\n}", DisplayFormatter.ToDisplayText(n).ReplaceLineEndings("\n"));
}
[Fact] public void ToDisplayText_FormatsNestedArraysAndScalars()
{
    var n = JsonNode.Parse("""{"a":[1,true,null],"b":{}}""");
    Assert.Equal("{\n  \"a\": [\n    1,\n    true,\n    null\n  ],\n  \"b\": {}\n}",
                 DisplayFormatter.ToDisplayText(n).ReplaceLineEndings("\n"));
}

// ErrorFormatterTests
[Fact] public void Format_PutsMessageBeforeStackAndIncludesInner()
{
    Exception ex;
    try { try { throw new InvalidOperationException("inner boom"); }
          catch (Exception i) { throw new IOException("outer boom", i); } }
    catch (Exception e) { ex = e; }
    var text = ErrorFormatter.Format(ex);
    Assert.StartsWith("System.IO.IOException: outer boom", text);
    Assert.True(text.IndexOf("at ") > text.IndexOf("outer boom"));
    Assert.Contains("System.InvalidOperationException: inner boom", text);
}
```

- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~JsonTextTests|FullyQualifiedName~DisplayFormatterTests|FullyQualifiedName~ErrorFormatterTests"` — expect build FAIL.
- [ ] **Step 3:** Implement the three classes. `TryParseLenient`: single pass that tracks "inside string" (honouring `\` escapes) and rewrites the three tokens outside strings to `null`, then `JsonNode.Parse` in try/catch. `DisplayFormatter`: hand-written recursive writer over `JsonObject`/`JsonArray`/`JsonValue` using a `StringBuilder`; numbers/bools via `ToJsonString()`. `ErrorFormatter`: for each exception in the inner chain, `"{FullName}: {Message}\n\n{StackTrace}"`, chained with `"\n\n--- Inner exception ---\n"`.
- [ ] **Step 4:** Run the Step 2 command — expect PASS.
- [ ] **Step 5:** Commit: `feat: add JSON parsing, display formatting and error formatting`

---

### Task 3: Metadata parser (tags → ComfyUI prompt/workflow)

ComfyUI stores metadata in these shapes, all of which must be recognised:
- PNG text chunks (`tEXt`/`iTXt`/`zTXt`) with keyword `prompt` / `workflow` — Task 5 emits these with the keyword as the tag key, so they hit the "tag literally named" rule below.
- WebP/JPEG EXIF strings with a prefix: `Model` = `prompt:{...}`, `Make` = `workflow:{...}` (other `key:{json}` prefixes are kept under their key).
- Video `comment` tag (any case) holding `{"prompt": ..., "workflow": ...}`, where the inner values may be JSON-encoded strings.
- A tag literally named `prompt` / `workflow` (any case) holding JSON.

**Files:**
- Create: `src/ComfyGenReader.Core/Metadata/MetadataResult.cs`, `src/ComfyGenReader.Core/Metadata/MetadataParser.cs`
- Create: `tests/ComfyGenReader.Core.Tests/Fixtures/ComfySamples.cs` — `const string ApiPrompt` (KSampler "3" seed 156680208700286, positive link `["6",0]` → CLIPTextEncode "6" text `"a cat\nwearing a hat"`, negative → node "7" text `"blurry"`) and `const string Workflow` (UI format: `nodes[]` with `type` `KSampler` `widgets_values[0]=156680208700286` and `CLIPTextEncode` `widgets_values[0]="a cat\nwearing a hat"`, plus `"links"`, `"version": 0.4`).
- Test: `tests/ComfyGenReader.Core.Tests/MetadataParserTests.cs`

**Interfaces:**
- Consumes: `JsonText.TryParseLenient`, `JsonText.ExpandEmbedded`, `JsonText.ToExportJson` (Task 2)
- Produces:
  - `sealed record MetadataResult(JsonObject Raw, JsonNode? Prompt, JsonNode? Workflow) { string? ExportJson { get; } }` — `ExportJson` = `ToExportJson(Workflow ?? Prompt)`, or `null` when both are null.
  - `static MetadataResult MetadataParser.Parse(IEnumerable<KeyValuePair<string,string>> tags)` — `Raw` maps each tag key to its expanded JSON node if parseable (after stripping a `word:` prefix whose remainder parses), else to the string. Duplicate keys get ` (2)`, ` (3)` suffixes.

- [ ] **Step 1: Write the failing tests**

```csharp
static KeyValuePair<string,string> T(string k, string v) => new(k, v);

[Fact] public void Parse_ExifPrefixedStrings()
{
    var r = MetadataParser.Parse([T("Exif IFD0/Model", "prompt:" + ComfySamples.ApiPrompt),
                                  T("Exif IFD0/Make", "workflow:" + ComfySamples.Workflow)]);
    Assert.Equal("KSampler", (string)r.Prompt!["3"]!["class_type"]!);
    Assert.NotNull(r.Workflow!["nodes"]);
    Assert.Equal(r.Workflow.ToJsonString(), JsonNode.Parse(r.ExportJson!)!.ToJsonString());
}
[Fact] public void Parse_VideoCommentWithDoubleEncodedValues()
{
    var comment = new JsonObject { ["prompt"] = ComfySamples.ApiPrompt, ["workflow"] = ComfySamples.Workflow }.ToJsonString();
    var r = MetadataParser.Parse([T("COMMENT", comment), T("encoder", "Lavf61.7.100")]);
    Assert.NotNull(r.Prompt!["3"]); Assert.NotNull(r.Workflow!["nodes"]);
    Assert.Equal("Lavf61.7.100", (string)r.Raw["encoder"]!);
}
[Fact] public void Parse_PromptOnly_ExportsPrompt()
{
    var r = MetadataParser.Parse([T("prompt", ComfySamples.ApiPrompt)]);
    Assert.Null(r.Workflow);
    Assert.NotNull(JsonNode.Parse(r.ExportJson!)!["3"]);
}
[Fact] public void Parse_NoComfyData_ExportIsNull()
{
    var r = MetadataParser.Parse([T("Exif IFD0/Make", "Canon"), T("Exif IFD0/Model", "EOS R5")]);
    Assert.Null(r.Prompt); Assert.Null(r.Workflow); Assert.Null(r.ExportJson);
    Assert.Equal("Canon", (string)r.Raw["Exif IFD0/Make"]!);
}
```

- [ ] **Step 2:** Run `dotnet test --filter FullyQualifiedName~MetadataParserTests` — expect build FAIL.
- [ ] **Step 3:** Implement. Discovery order for `Prompt`/`Workflow`: (1) a Raw key equal to `prompt`/`workflow` (case-insensitive) or whose stripped prefix was `prompt`/`workflow`; (2) a property named `prompt`/`workflow` inside any top-level Raw object. Only accept `JsonObject` values.
- [ ] **Step 4:** Run the Step 2 command — expect PASS.
- [ ] **Step 5:** Commit: `feat: parse ComfyUI prompt and workflow from metadata tags`

---

### Task 4: Seed and prompt extraction

**Files:**
- Create: `src/ComfyGenReader.Core/Metadata/ComfyFields.cs`
- Test: `tests/ComfyGenReader.Core.Tests/ComfyFieldsTests.cs`

**Interfaces:**
- Produces: `sealed record ComfyHighlights(string Seed, string Prompt)`; `static ComfyHighlights ComfyFields.Extract(JsonNode? apiPrompt, JsonNode? workflow)` — empty strings when not found.

Rules (API prompt preferred; workflow is fallback only when the API prompt yields nothing):
- Iterate API nodes ordered by numeric id. **Seed**: every node with `inputs.seed` or `inputs.noise_seed`; distinct values joined with `", "`. **Prompt**: first node with `inputs.positive`; resolve to its node's `text`, then `text_g`. If none, first node with `class_type == "CLIPTextEncode"`.
- A link is a 2-element array `[ "<nodeId>", <int> ]`. Resolving a link to a scalar reads the target node's first present input among `seed, noise_seed, value, int, number, text, string`. Follow at most 5 hops.
- Workflow fallback: first node whose `type` contains `KSampler` → `widgets_values[0]` as seed; first `CLIPTextEncode` → `widgets_values[0]` as prompt.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Extract_FromApiPrompt()
{
    var h = ComfyFields.Extract(JsonNode.Parse(ComfySamples.ApiPrompt), null);
    Assert.Equal("156680208700286", h.Seed); Assert.Equal("a cat\nwearing a hat", h.Prompt);
}
[Fact] public void Extract_FallsBackToWorkflow()
{
    var h = ComfyFields.Extract(null, JsonNode.Parse(ComfySamples.Workflow));
    Assert.Equal("156680208700286", h.Seed); Assert.Equal("a cat\nwearing a hat", h.Prompt);
}
[Fact] public void Seed_FollowsLinkToPrimitive()
{
    var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"seed":["12",0]}},"12":{"class_type":"PrimitiveInt","inputs":{"value":42}}}""");
    Assert.Equal("42", ComfyFields.Extract(p, null).Seed);
}
[Fact] public void Prompt_FollowsTextLink()
{
    var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"positive":["6",0]}},"6":{"class_type":"CLIPTextEncode","inputs":{"text":["9",0]}},"9":{"class_type":"PrimitiveString","inputs":{"value":"linked text"}}}""");
    Assert.Equal("linked text", ComfyFields.Extract(p, null).Prompt);
}
[Fact] public void Extract_MultipleSamplersJoinsDistinctSeeds()
{
    var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"seed":1}},"4":{"class_type":"KSamplerAdvanced","inputs":{"noise_seed":2}},"5":{"class_type":"KSampler","inputs":{"seed":1}}}""");
    Assert.Equal("1, 2", ComfyFields.Extract(p, null).Seed);
}
[Fact] public void Extract_CyclicLinks_DoesNotHang()
{
    var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"seed":["4",0]}},"4":{"class_type":"X","inputs":{"value":["3",0]}}}""");
    Assert.Equal("", ComfyFields.Extract(p, null).Seed);
}
```

- [ ] **Step 2:** Run `dotnet test --filter FullyQualifiedName~ComfyFieldsTests` — expect build FAIL.
- [ ] **Step 3:** Implement `ComfyFields.Extract` per the rules above. Non-numeric node ids sort after numeric ones, ordinal.
- [ ] **Step 4:** Run the Step 2 command — expect PASS.
- [ ] **Step 5:** Commit: `feat: extract seed and positive prompt from ComfyUI data`

---

### Task 5: Image metadata reader

**Files:**
- Modify: `src/ComfyGenReader.Core/ComfyGenReader.Core.csproj` (add `MetadataExtractor`, latest stable, exact version)
- Create: `src/ComfyGenReader.Core/Metadata/ImageTagReader.cs`
- Create: `tests/ComfyGenReader.Core.Tests/Fixtures/ExifFixtures.cs`
- Test: `tests/ComfyGenReader.Core.Tests/ImageTagReaderTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<KeyValuePair<string,string>> ImageTagReader.Read(string path)` — key `"{Directory.Name}/{Tag.Name}"`, value = full string value. For ASCII/UserComment tags read the raw string (`directory.GetString(tagType)` / `GetObject` decoded), **not** `Tag.Description`, which truncates and reformats. Also include JPEG `Jpeg Comment` and XMP property values. **PNG text chunks** (`PngDirectory.TagTextualData`, a list of keyword/text pairs from `tEXt`/`iTXt`/`zTXt`) are emitted one entry per pair with key = the bare keyword (e.g. `prompt`), value = full text.
- Test helper: `ExifFixtures.WriteJpeg(string path, IDictionary<ushort,string> ifd0Ascii)`, `ExifFixtures.WriteWebP(string path, IDictionary<ushort,string> ifd0Ascii)`, and `ExifFixtures.WritePng(string path, IDictionary<string,string> textChunks)` (signature + valid 1×1 `IHDR`, one `tEXt` chunk per entry as Latin-1 `keyword\0text`, minimal `IDAT`, `IEND`; CRC-32 via `System.IO.Hashing.Crc32` test-only package). Build one little-endian TIFF blob (`II*\0`, IFD0 at offset 8, ASCII entries type 2 with values stored after the IFD, next-IFD = 0). JPEG = `FFD8` + APP1 (`FFE1`, length, `Exif\0\0` + TIFF) + `FFD9`. WebP = `RIFF` + size + `WEBP` + `VP8X` chunk (10 bytes, EXIF flag `0x08`, 1×1 canvas) + `EXIF` chunk (TIFF, padded to even length).

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory, InlineData(".jpg"), InlineData(".webp")]
public void Read_ReturnsFullExifStrings(string ext)
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ext);
    var tags = new Dictionary<ushort,string> { [0x0110] = "prompt:" + ComfySamples.ApiPrompt,
                                               [0x010F] = "workflow:" + ComfySamples.Workflow };
    if (ext == ".jpg") ExifFixtures.WriteJpeg(path, tags); else ExifFixtures.WriteWebP(path, tags);
    var read = ImageTagReader.Read(path);
    Assert.Contains(read, kv => kv.Key == "Exif IFD0/Model" && kv.Value == "prompt:" + ComfySamples.ApiPrompt);
    var r = MetadataParser.Parse(read);
    Assert.NotNull(r.Prompt); Assert.NotNull(r.Workflow);
}

[Fact]
public void Read_PngTextChunks_KeyedByKeyword()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
    ExifFixtures.WritePng(path, new Dictionary<string,string> { ["prompt"] = ComfySamples.ApiPrompt,
                                                                ["workflow"] = ComfySamples.Workflow });
    var read = ImageTagReader.Read(path);
    Assert.Contains(read, kv => kv.Key == "prompt" && kv.Value == ComfySamples.ApiPrompt);
    var r = MetadataParser.Parse(read);
    Assert.NotNull(r.Prompt); Assert.NotNull(r.Workflow);
}
```

- [ ] **Step 2:** Run `dotnet test --filter FullyQualifiedName~ImageTagReaderTests` — expect build FAIL.
- [ ] **Step 3:** Implement `ExifFixtures` and `ImageTagReader` (`ImageMetadataReader.ReadMetadata(path)`).
- [ ] **Step 4:** Run the Step 2 command — expect PASS. Run `dotnet build` — expect no `NU190x` audit errors.
- [ ] **Step 5:** Commit: `feat: read EXIF/XMP metadata from JPEG and WebP`

---

### Task 6: FFmpeg tools (video tags, frame grab)

**Files:**
- Create: `tools/ffmpeg.lock.json` (`{"url": "...", "sha256": "..."}`), `tools/get-ffmpeg.ps1`
- Create: `src/ComfyGenReader.Core/Media/FfmpegTools.cs`
- Modify: `src/ComfyGenReader/ComfyGenReader.csproj` and the test csproj — `<None Include="..\..\third_party\ffmpeg\**" Link="lib\ffmpeg\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />`
- Test: `tests/ComfyGenReader.Core.Tests/FfmpegToolsTests.cs`

**Interfaces:**
- Produces:
  - `static string FfmpegTools.Directory` = `Path.Combine(AppContext.BaseDirectory, "lib", "ffmpeg")`
  - `static Task<IReadOnlyList<KeyValuePair<string,string>>> FfmpegTools.ReadTagsAsync(string path, CancellationToken ct = default)` — runs `ffprobe -v error -print_format json -show_format -show_streams <path>`, returns `format.tags` then each stream's `tags` (keys prefixed `stream{index}/`). Throws `InvalidOperationException` with ffprobe's stderr on non-zero exit.
  - `static Task<byte[]?> FfmpegTools.GrabFramePngAsync(string path, TimeSpan at, int width, CancellationToken ct)` — runs `ffmpeg -v error -ss <seconds, invariant> -i <path> -frames:v 1 -vf scale=<width>:-2 -f image2pipe -c:v png -`; returns stdout bytes, `null` if empty. Kills the process on cancellation.
  - Both use `ProcessStartInfo.ArgumentList` (no manual quoting), `CreateNoWindow = true`, and read stdout/stderr concurrently.

- [ ] **Step 1:** Pick the current stable FFmpeg release from BtbN/FFmpeg-Builds in the `win64-lgpl-shared` flavour; confirm on NVD that the release has no unpatched critical CVEs. Record URL + SHA256 in `tools/ffmpeg.lock.json`. `get-ffmpeg.ps1` downloads, verifies SHA256 (fail on mismatch), and extracts `bin\*` into `third_party\ffmpeg\`. Run it; expect `third_party\ffmpeg\ffprobe.exe` and `ffmpeg.exe` exist.

- [ ] **Step 2: Write the failing tests** (fixtures generated with the bundled ffmpeg in a class fixture)

```csharp
// Fixture: for ext in mp4, webm, avi run
// ffmpeg -v error -f lavfi -i testsrc=duration=2:size=64x64:rate=10 -metadata comment=<json> -y <tmp>.<ext>
// where json = {"prompt": ComfySamples.ApiPrompt, "workflow": ComfySamples.Workflow} serialized.

[Theory, InlineData("mp4"), InlineData("webm"), InlineData("avi")]
public async Task ReadTags_FindsComfyComment(string ext)
{
    var r = MetadataParser.Parse(await FfmpegTools.ReadTagsAsync(fx.PathFor(ext)));
    Assert.NotNull(r.Prompt); Assert.NotNull(r.Workflow);
}
[Fact] public async Task GrabFrame_ReturnsPng()
{
    var png = await FfmpegTools.GrabFramePngAsync(fx.PathFor("mp4"), TimeSpan.FromSeconds(1), 64, default);
    Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png![..4]);
}
[Fact] public async Task ReadTags_NotAVideo_Throws() =>
    await Assert.ThrowsAsync<InvalidOperationException>(() => FfmpegTools.ReadTagsAsync(fx.TextFilePath));
```

- [ ] **Step 3:** Run `dotnet test --filter FullyQualifiedName~FfmpegToolsTests` — expect build FAIL.
- [ ] **Step 4:** Implement `FfmpegTools`.
- [ ] **Step 5:** Run the Step 3 command — expect PASS. (If AVI drops the `comment` tag, ffprobe exposes it as `comment` from RIFF `ICMT`; adjust nothing in the parser — it is case-insensitive.)
- [ ] **Step 6:** Commit (lock file and script only; binaries are gitignored): `feat: read video metadata and frames via bundled ffmpeg`

---

### Task 7: Recent files and folder navigation

**Files:**
- Create: `src/ComfyGenReader.Core/RecentFiles.cs`, `src/ComfyGenReader.Core/FolderNavigator.cs`
- Test: `tests/ComfyGenReader.Core.Tests/RecentFilesTests.cs`, `FolderNavigatorTests.cs`

**Interfaces:**
- Produces:
  - `sealed class RecentFiles { RecentFiles(string filePath, int capacity = 10); IReadOnlyList<string> Items { get; } void Add(string path); event EventHandler? Changed; static string DefaultPath { get; } }` — `DefaultPath` = `<AppContext.BaseDirectory>\data\history.json`. File format: `{ "recent": [ "C:\\...", ... ] }`. `Add` moves an existing entry (case-insensitive full-path match) to the top, trims to capacity, saves (creating `data\`), raises `Changed`. Load and save failures are swallowed.
  - `static class FolderNavigator { string? Next(string current); string? Previous(string current); }` — siblings in `current`'s folder where `SupportedFiles.IsSupported`, sorted with Windows Explorer order (`StrCmpLogicalW` from `shlwapi.dll` via `LibraryImport`). Returns `null` at either end (no wrap). If `current` no longer exists, position by where it would sort.

- [ ] **Step 1: Write the failing tests**

```csharp
// RecentFilesTests (each test uses its own temp dir)
[Fact] public void Add_MostRecentFirst_DedupesAndCapsAt10()
{
    var rf = new RecentFiles(file);
    for (int i = 0; i < 12; i++) rf.Add($@"C:\x\{i}.jpg");
    rf.Add(@"c:\X\5.JPG");
    Assert.Equal(10, rf.Items.Count);
    Assert.Equal(@"c:\X\5.JPG", rf.Items[0]);
    Assert.Single(rf.Items, p => p.Equals(@"C:\x\5.jpg", StringComparison.OrdinalIgnoreCase));
}
[Fact] public void Add_PersistsAcrossInstances()
{
    new RecentFiles(file).Add(@"C:\x\a.jpg");
    Assert.Equal([@"C:\x\a.jpg"], new RecentFiles(file).Items);
}
[Fact] public void Load_CorruptFile_StartsEmpty()
{
    File.WriteAllText(file, "{not json");
    Assert.Empty(new RecentFiles(file).Items);
}
[Fact] public void Add_WhenSaveFails_StillUpdatesInMemory()
{
    using var lockHandle = File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    var rf = new RecentFiles(file);
    rf.Add(@"C:\x\a.jpg");
    Assert.Equal([@"C:\x\a.jpg"], rf.Items);
}

// FolderNavigatorTests: temp dir with a2.jpg, a10.mp4, a1.webp, notes.txt, b.gif
[Fact] public void Next_UsesNaturalOrderAndSkipsUnsupported()
{
    Assert.Equal(P("a2.jpg"), FolderNavigator.Next(P("a1.webp")));
    Assert.Equal(P("a10.mp4"), FolderNavigator.Next(P("a2.jpg")));
    Assert.Null(FolderNavigator.Next(P("a10.mp4")));
}
[Fact] public void Previous_AtStart_ReturnsNull() => Assert.Null(FolderNavigator.Previous(P("a1.webp")));
[Fact] public void Next_FromDeletedFile_UsesSortPosition() =>
    Assert.Equal(P("a10.mp4"), FolderNavigator.Next(P("a3.jpg")));
```

- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~RecentFilesTests|FullyQualifiedName~FolderNavigatorTests"` — expect build FAIL.
- [ ] **Step 3:** Implement both classes (`System.Text.Json` for the history file; `[LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]` for `StrCmpLogicalW`; Core csproj sets `AllowUnsafeBlocks=true` if the generator requires it).
- [ ] **Step 4:** Run the Step 2 command — expect PASS.
- [ ] **Step 5:** Commit: `feat: add recent-files history and folder navigation`

---

### Task 8: ReaderSession view-model

**Files:**
- Create: `src/ComfyGenReader.Core/Metadata/IMetadataLoader.cs`, `src/ComfyGenReader.Core/ReaderSession.cs`
- Test: `tests/ComfyGenReader.Core.Tests/ReaderSessionTests.cs`

**Interfaces:**
- Consumes: `SupportedFiles`, `MetadataParser`, `ComfyFields`, `DisplayFormatter`, `ErrorFormatter`, `ImageTagReader`, `FfmpegTools`, `RecentFiles`, `FolderNavigator`
- Produces:
  - `interface IMetadataLoader { Task<MetadataResult> LoadAsync(string path, CancellationToken ct); }` and `sealed class MetadataLoader : IMetadataLoader` (images → `ImageTagReader` on `Task.Run`; videos → `FfmpegTools.ReadTagsAsync`; then `MetadataParser.Parse`).
  - `enum OpenSource { User, Navigation }`
  - `sealed class ReaderSession : INotifyPropertyChanged`:
    - `ReaderSession(IMetadataLoader loader, RecentFiles recent)`
    - Properties (all raise `PropertyChanged`): `string PathText { get; set; }`, `string? CurrentFile`, `MediaKind CurrentKind`, `string SeedText`, `string PromptText`, `string RawText`, `string? ExportJson`, `bool CanExport`, `bool CanGoPrevious`, `bool CanGoNext`, `IReadOnlyList<string> Recent`
    - `Task<bool> OpenAsync(string path, OpenSource source)` — trims whitespace and surrounding `"`; unsupported extension → `RawText = "Unsupported file type: <ext>"`, returns false, nothing else changes; missing file → `RawText = "File not found: <path>"`, returns false. On success sets `CurrentFile`, `CurrentKind`, `PathText`; adds to `recent` only when `source == User`; sets `RawText = DisplayFormatter.ToDisplayText(result.Raw)`, highlights from `ComfyFields.Extract`, `ExportJson = result.ExportJson`. Loader exception → `ShowError(ex)` but the preview (`CurrentFile`) still updates. Cancels any in-flight load when a new one starts.
    - `Task GoNextAsync()`, `Task GoPreviousAsync()` — `OpenAsync(FolderNavigator.Next/Previous(CurrentFile), OpenSource.Navigation)`.
    - `void ShowError(Exception ex)` — `RawText = ErrorFormatter.Format(ex)`.

- [ ] **Step 1: Write the failing tests** (fake loader returns a canned `MetadataResult` or throws; files created in a temp dir)

```csharp
[Fact] public async Task Open_User_AddsToHistoryAndFillsHighlights()
{
    Assert.True(await s.OpenAsync(jpg, OpenSource.User));
    Assert.Equal(jpg, s.CurrentFile); Assert.Equal(jpg, s.PathText); Assert.Equal(jpg, s.Recent[0]);
    Assert.Equal("156680208700286", s.SeedText); Assert.Equal("a cat\nwearing a hat", s.PromptText);
    Assert.True(s.CanExport);
}
[Fact] public async Task Navigation_DoesNotAddToHistory()
{
    await s.OpenAsync(jpg, OpenSource.User);
    await s.GoNextAsync();
    Assert.Equal(nextFile, s.CurrentFile);
    Assert.Equal([jpg], s.Recent);
}
[Fact] public async Task Open_TrimsQuotesAndWhitespace() =>
    Assert.True(await s.OpenAsync($"  \"{jpg}\" ", OpenSource.User));
[Fact] public async Task Open_Unsupported_LeavesStateAndReports()
{
    Assert.False(await s.OpenAsync(gif, OpenSource.User));
    Assert.Null(s.CurrentFile); Assert.Equal("Unsupported file type: .gif", s.RawText); Assert.Empty(s.Recent);
}
[Fact] public async Task Open_Missing_ReportsNotFound()
{
    Assert.False(await s.OpenAsync(@"C:\nope\gone.jpg", OpenSource.User));
    Assert.Equal(@"File not found: C:\nope\gone.jpg", s.RawText);
}
[Fact] public async Task Open_LoaderThrows_ShowsErrorAndKeepsPreview()
{
    loader.Throw = new IOException("disk gone");
    await s.OpenAsync(jpg, OpenSource.User);
    Assert.StartsWith("System.IO.IOException: disk gone", s.RawText); Assert.Equal(jpg, s.CurrentFile);
}
[Fact] public async Task Open_NoComfyData_DisablesExport()
{
    loader.Result = MetadataParser.Parse([new("Exif IFD0/Make", "Canon")]);
    await s.OpenAsync(jpg, OpenSource.User);
    Assert.False(s.CanExport); Assert.Equal("", s.SeedText); Assert.Contains("Canon", s.RawText);
}
```

- [ ] **Step 2:** Run `dotnet test --filter FullyQualifiedName~ReaderSessionTests` — expect build FAIL.
- [ ] **Step 3:** Implement `MetadataLoader` and `ReaderSession`.
- [ ] **Step 4:** Run the Step 2 command, then the full `dotnet test` — expect all PASS.
- [ ] **Step 5:** Commit: `feat: add ReaderSession view-model`

---

### Task 9: WPF shell (layout, open/browse/drop, image preview, export, crash safety)

**Files:**
- Modify: `src/ComfyGenReader/App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `ReaderSession` (Task 8), `RecentFiles.DefaultPath`, `SupportedFiles.DialogFilter`, `MetadataLoader`

Layout decisions:
- Window: `Title="ComfyGenReader"`, `Width=1200 Height=800 MinWidth=400 MinHeight=400`, `DataContext` = `ReaderSession`.
- Root grid rows `Auto, *`; row 1 columns `6*, 4*`.
- Top: row of `TextBox` (bound `PathText`, `UpdateSourceTrigger=PropertyChanged`, Enter = GO), `Browse…` button, `GO` button. Below: non-editable `ComboBox` stretched full width, `ItemsSource={Binding Recent}`, placeholder text "Recent files"; selecting an item calls `OpenAsync(item, User)` then resets `SelectedIndex = -1`.
- Left: rows `*, Auto (video controls host, Task 10), Auto (nav)`. Row 0: `Image` with `Stretch="Uniform"` `StretchDirection="Both"`, plus an empty-state panel ("⬇" icon glyph via Segoe MDL2 Assets `&#xE896;` + text "Drag a file here") visible when `CurrentFile` is null. Nav row: `◀ Previous` / `Next ▶` buttons bound to `CanGoPrevious`/`CanGoNext`.
- Right: rows `Auto, *, Auto`. Highlights `GroupBox`: "Seed" read-only `TextBox`; "Prompt" read-only `TextBox` `TextWrapping=Wrap`, `AcceptsReturn=True`, `VerticalScrollBarVisibility=Visible`, height = 5 lines (`MinLines=5 MaxLines=5`). Raw `GroupBox`: read-only `TextBox`, `FontFamily="Consolas"`, both scroll bars `Auto`, no wrap. Bottom: small right-aligned `Export` button bound `IsEnabled=CanExport`.
- All read-only text boxes remain selectable/copyable.

- [ ] **Step 1:** Implement `App.xaml.cs`: handle `DispatcherUnhandledException` (`e.Handled = true`, route to `session.ShowError`), `TaskScheduler.UnobservedTaskException` (`SetObserved`, marshal to UI), `AppDomain.CurrentDomain.UnhandledException` (marshal to UI best-effort). If no `MainWindow` exists yet, show the formatted text in a `MessageBox`. Every `async void` event handler in the app wraps its body in `try { … } catch (Exception ex) { session.ShowError(ex); }`.
- [ ] **Step 2:** Implement the layout and handlers in `MainWindow`: Browse (`Microsoft.Win32.OpenFileDialog`, filter `SupportedFiles.DialogFilter`, opens with `User`), GO, drop (left frame `AllowDrop=True`, `Background="Transparent"`; `DragOver` sets `DragDropEffects.Copy` only if any dropped path `IsSupported`, else `None`; `Drop` opens the first supported path with `User`), Export (`SaveFileDialog`, default name `<file name without ext>.json`, writes `ExportJson` as UTF-8 without BOM).
- [ ] **Step 3:** Image preview: when `CurrentFile`/`CurrentKind` changes to `Image`, load a `BitmapImage` with `CacheOption=OnLoad` (file not locked) inside try/catch. If WIC can't decode (WebP codec absent), fall back to `FfmpegTools.GrabFramePngAsync(path, TimeSpan.Zero, 4096, ct)` decoded from a `MemoryStream`; if that fails, `ShowError`.
- [ ] **Step 4:** Run `dotnet build` (0 warnings), then `dotnet run --project src/ComfyGenReader`. Manually verify with the Task 5 JPEG/WebP fixtures (copy two into one folder) and a real ComfyUI image:
  - Window cannot shrink below 400×400; left/right split stays 60/40 when resizing.
  - Drop a JPEG → image fills left frame without distortion, path box updates, it appears first in the dropdown, Seed/Prompt fill, Raw shows prompt text with real line breaks.
  - Next/Previous step through the folder; dropdown does not gain those files.
  - Drop a `.gif` → cursor shows "not allowed"; type a `.gif` path + GO → Raw shows "Unsupported file type: .gif".
  - Drop a ComfyUI PNG → Seed/Prompt fill from its text chunks.
  - Export → saved file loads in ComfyUI (drag the `.json` onto the ComfyUI canvas) and shows the workflow.
  - Restart app → dropdown still lists recent files; `data\history.json` exists next to the exe.
- [ ] **Step 5:** Commit: `feat: add main window, image preview, export and global error handling`

---

### Task 10: Video player control

**Files:**
- Modify: `src/ComfyGenReader/ComfyGenReader.csproj` — add `LibVLCSharp.WPF` and `VideoLAN.LibVLC.Windows` (latest stable, exact versions), set `<VlcWindowsX64TargetDir>lib\libvlc</VlcWindowsX64TargetDir>` and `<VlcWindowsX86IncludeFiles Include="none" />` (x64 only).
- Create: `src/ComfyGenReader/Controls/VideoPlayer.xaml`, `VideoPlayer.xaml.cs`
- Modify: `MainWindow.xaml(.cs)` — host `VideoPlayer` in left row 0 (overlaying `Image`, visibility by `CurrentKind`) and its controls row in left row 1.

**Interfaces:**
- Consumes: `FfmpegTools.GrabFramePngAsync`
- Produces: `VideoPlayer : UserControl { void Load(string path); void Unload(); event EventHandler<Exception>? Failed; }` — `MainWindow` calls `Load` on `CurrentKind == Video`, `Unload` otherwise and on close; `Failed` → `session.ShowError`.

Decisions:
- `LibVLCSharp.Shared.Core.Initialize(Path.Combine(AppContext.BaseDirectory, "lib", "libvlc"))` once at startup. One `LibVLC` (`"--no-video-title-show"`) and one `MediaPlayer` for the app lifetime; dispose on exit.
- Video scales to fit by LibVLC default (`Scale = 0`, `AspectRatio = null`) — uniform, no distortion.
- `MediaPlayer.Mute = true` on every load; Mute button shows 🔇/🔊 state.
- Controls row: `Play/Pause` toggle, `Stop` (stop + seek bar to 0), seek `Slider` (0–1, updated from `PositionChanged` via `Dispatcher`, user drag sets `MediaPlayer.Position` on release), `Mute` toggle, time label `mm:ss / mm:ss`.
- Scrub preview: on slider `MouseMove`/drag, compute time = `x / ActualWidth × Length`; show a `Popup` (`Placement=Top`, follows cursor X) with a 160px-wide `Image`. Debounce 120 ms, cancel the previous grab, cache frames by whole second per file. Hide on `MouseLeave`.
- Drag-drop over a playing video: LibVLC's `VideoView` is a native HWND that swallows WPF drag events, so set `VideoView.Content` to a `Grid` with `Background="#01000000"` and `AllowDrop=True` that forwards to the same `DragOver`/`Drop` handlers as the left frame.
- All LibVLC event callbacks marshal to the UI thread and wrap their bodies in try/catch → `Failed`.

- [ ] **Step 1:** Implement `VideoPlayer` and wire it into `MainWindow`.
- [ ] **Step 2:** Run `dotnet build` — expect 0 warnings and `bin\...\lib\libvlc\libvlc.dll` present (if the `VlcWindowsX64TargetDir` override is ignored by the package version, keep its default folder and change only the `Core.Initialize` path).
- [ ] **Step 3:** Manually verify with the Task 6 fixtures (mp4, webm, avi) and a real ComfyUI VHS video:
  - Each plays; starts muted; Mute toggles audio; Stop returns to 0.
  - Hovering/dragging the seek bar shows a frame preview that changes with position.
  - Video fills the left frame without distortion when the window is resized.
  - Seed/Prompt/Raw fill from the video's metadata; Export works.
  - Dropping a new file onto the playing video loads it; Next from a video to an image stops playback and shows the image.
- [ ] **Step 4:** Commit: `feat: add video playback with scrub preview`

---

### Task 11: Portable publish and vulnerability check

**Files:**
- Modify: `src/ComfyGenReader/ComfyGenReader.csproj` — `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true`, `PublishTrimmed=false`, `ApplicationIcon` optional.
- Create: `tools/publish.ps1`, `THIRD-PARTY-NOTICES.md` (FFmpeg LGPL, LibVLC LGPL, MetadataExtractor Apache-2.0, with versions and source URLs)

**Interfaces:**
- Produces: `artifacts\ComfyGenReader-win-x64.zip` containing `ComfyGenReader\` with `ComfyGenReader.exe`, `lib\libvlc\`, `lib\ffmpeg\`, `THIRD-PARTY-NOTICES.md`.

- [ ] **Step 1:** `tools/publish.ps1`: runs `tools/get-ffmpeg.ps1` if `third_party\ffmpeg` is missing, `dotnet publish src/ComfyGenReader -c Release -o artifacts/ComfyGenReader`, copies the notices file, deletes `*.pdb`, zips with `Compress-Archive`.
- [ ] **Step 2:** Run `dotnet list package --vulnerable --include-transitive` — expect "has no vulnerable packages" for every project. For the pinned FFmpeg and the LibVLC native version (from the package), search NVD for CVEs affecting that version; expect none rated Critical that are unpatched. Record the result (date, versions, finding) in `THIRD-PARTY-NOTICES.md`.
- [ ] **Step 3:** Run `pwsh tools/publish.ps1` — expect the zip, and the root of `artifacts\ComfyGenReader\` contains only `ComfyGenReader.exe`, `lib\`, and the notices file (no loose managed DLLs).
- [ ] **Step 4:** Portability check: extract the zip to a different path (e.g. `D:\tmp\cgr-test\`) on a machine or user without the .NET SDK if available, run the exe, open an image and a video, confirm `data\history.json` is created next to the exe. Mark the folder read-only, relaunch, open a file — expect no crash and history simply not saved.
- [ ] **Step 5:** Commit: `build: add portable publish script and third-party notices`
